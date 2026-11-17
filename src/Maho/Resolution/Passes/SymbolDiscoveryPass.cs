using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

/// <summary>
/// Resolution Pass 1: Symbol Discovery.
/// <para>
/// Walks all compilation units in the project syntax trees to discover and declare top-level and nested symbols
/// (namespaces, classes, structs, interfaces, enums, sum types, methods, fields, properties, functions, and macros).
/// Constructs the lexical <see cref="Scope"/> hierarchy, populates symbol tables, and binds syntax nodes to their scopes.
/// </para>
/// </summary>
internal sealed partial class SymbolDiscoveryPass : ResolutionPass
{
    private ResolutionContext context = null!;

    /// <summary>
    /// Executes symbol discovery across all syntax trees in the compilation.
    /// </summary>
    /// <param name="context">The shared compilation resolution context.</param>
    public override void Resolve(ResolutionContext context)
    {
        this.context = context;

        if (context.Options?.GlobalAliases is { Count: > 0 } globalAliases)
        {
            context.CurrentTreeFilePath = context.Options?.ProjectFilePath ?? "<global-aliases>";
            RegisterGlobalAliases(globalAliases);
        }

        foreach (var tree in context.SyntaxTrees)
        {
            context.CurrentTreeFilePath = tree.FilePath;
            ResolveCompilationUnit(tree.Root);
        }
        context.CurrentTreeFilePath = null;
    }

    private void RegisterGlobalAliases(IReadOnlyDictionary<string, string> globalAliases)
    {
        foreach (var (aliasName, target) in globalAliases)
        {
            string aliasSource = $"using {aliasName} = {target};";
            var text = new SourceText(aliasSource, context.Options?.ProjectFilePath ?? "<global-aliases>");
            var dm = new DiagnosticsManager(text);
            var lexer = new Lexer(text, dm);
            lexer.Lex();
            var parser = new Parser(text, dm);
            var unit = parser.Parse(lexer.Tokens);

            if (dm.Diagnostics.Count > 0)
            {
                foreach (var diag in dm.Diagnostics)
                    context.Diagnostics?.Report(diag);

                continue;
            }

            if (unit.Members.Count > 0 && unit.Members[0] is TopLevelAliasDeclaration topAlias)
            {
                var symbol = ResolveTopLevelAliasDeclaration(topAlias.Alias, context.GlobalScope, context.GlobalNamespace);
                symbol.IsGlobalAlias = true;
            }
        }
    }

    private void ResolveCompilationUnit(CompilationUnit unit)
    {
        var fileScope = context.CreateScope(context.GlobalScope);
        context.RegisterSyntaxScope(unit, fileScope);

        foreach (var usingDirective in unit.Usings)
            ResolveUsingDirective(usingDirective, fileScope);

        FunctionSymbol? topLevelMain = null;
        var topLevelMainScope = fileScope;

        if (unit.EnablesTopLevelStatements)
        {
            topLevelMainScope = context.CreateScope(fileScope);
            topLevelMain = context.CreateFunctionSymbol(fileScope, new SymbolPart("Main"), context.GlobalNamespace, syntax: null);
            ResolutionContext.BindChildScope(fileScope, topLevelMain, topLevelMainScope);
        }

        ResolveTopLevelScope(unit.Members, fileScope, context.GlobalNamespace, topLevelMain, topLevelMainScope);
    }

    private NamespaceTrieNode ResolveTopLevelScope(IReadOnlyList<TopLevel> members, Scope scope, NamespaceTrieNode containingNamespace,
                                                   FunctionSymbol? topLevelMain, Scope topLevelMainScope)
    {
        var currentNamespace = containingNamespace;

        foreach (var member in members)
            currentNamespace = ResolveTopLevel(member, scope, currentNamespace, topLevelMain, topLevelMainScope);

        return currentNamespace;
    }

    private NamespaceTrieNode ResolveTopLevel(TopLevel topLevel, Scope scope, NamespaceTrieNode containingNamespace,
                                              FunctionSymbol? topLevelMain, Scope topLevelMainScope)
    {
        switch (topLevel)
        {
            case NamespaceDeclaration declaration:
                return ResolveNamespaceDeclaration(declaration, scope, containingNamespace, topLevelMain, topLevelMainScope);
            case TopLevelBlockDeclaration block:
                {
                    Scope blockScope = context.CreateScope(scope);
                    context.RegisterSyntaxScope(block, blockScope);
                    ResolveTopLevelScope(block.Members, blockScope, containingNamespace, HasGlobalModifier(block) ? null : topLevelMain, topLevelMainScope);
                    return containingNamespace;
                }
            case TopLevelUsingDirective directive:
                ResolveUsingDirective(directive.Directive, scope);
                return containingNamespace;
            case TopLevelAttributeDeclaration declaration:
                ResolveTopLevelAttributeDeclaration(declaration.Attribute, scope, containingNamespace);
                return containingNamespace;
            case TopLevelTypeDeclaration declaration:
                ResolveTopLevelTypeDeclaration(declaration.Type, scope, containingNamespace);
                return containingNamespace;
            case TopLevelLiteralMacroDeclaration:
                return containingNamespace;
            case TopLevelMacroDeclaration declaration:
                ResolveTopLevelMacroDeclaration(declaration.Macro, scope, containingNamespace);
                return containingNamespace;
            case TopLevelAliasDeclaration declaration:
                ResolveTopLevelAliasDeclaration(declaration.Alias, scope, containingNamespace);
                return containingNamespace;
            case TopLevelFunctionDeclaration declaration:
                ResolveTopLevelFunctionDeclaration(declaration.Function, scope, containingNamespace);
                return containingNamespace;
            case TopLevelVariableDeclaration declaration:
                ResolveTopLevelVariableDeclaration(declaration.Declaration, scope, containingNamespace, topLevelMain, topLevelMainScope);
                return containingNamespace;
            case TopLevelStatement statement when topLevelMain is not null:
                DiscoverTopLevelLabels(statement, topLevelMainScope, ResolutionContext.GetHandle(topLevelMain));
                return containingNamespace;
            default:
                return containingNamespace;
        }
    }

    private void ResolveUsingDirective(UsingDirective directive, Scope scope)
    {
        var nsNode = GetOrDeclareNamespace(context.GlobalNamespace, directive.Namespace);
        if (!scope.UsingNamespaces.Contains(nsNode))
            scope.UsingNamespaces.Add(nsNode);
    }

    private static bool HasGlobalModifier(TopLevelBlockDeclaration block)
    {
        foreach (Token modifier in block.Modifiers)
            if (modifier.MatchingKind is MatchingKeywordKind.Global)
                return true;

        return false;
    }

    private void DiscoverTopLevelLabels(TopLevelStatement statement, Scope scope, SymbolHandle containingFunction)
    {
        switch (statement)
        {
            case TopLevelLabelStatement label:
                context.CreateLabelSymbol(scope, new SymbolPart(label.Identifier), containingFunction, label);
                break;
        }
    }

    private NamespaceTrieNode ResolveNamespaceDeclaration(NamespaceDeclaration declaration, Scope scope, NamespaceTrieNode containingNamespace,
                                                           FunctionSymbol? topLevelMain, Scope topLevelMainScope)
    {
        var declaredNamespace = GetOrDeclareNamespace(containingNamespace, declaration.Name);

        if (declaration.Body is NamespaceEmptyBody)
        {
            if (!scope.UsingNamespaces.Contains(declaredNamespace))
                scope.UsingNamespaces.Add(declaredNamespace);
            return declaredNamespace;
        }

        var body = (NamespaceBlockBody)declaration.Body;
        var blockScope = context.CreateScope(scope);
        if (!blockScope.UsingNamespaces.Contains(declaredNamespace))
            blockScope.UsingNamespaces.Add(declaredNamespace);

        foreach (var usingDirective in body.Usings)
            ResolveUsingDirective(usingDirective, blockScope);

        context.RegisterSyntaxScope(body, blockScope);

        ResolveTopLevelScope(body.Members, blockScope, declaredNamespace, topLevelMain, topLevelMainScope);
        return containingNamespace;
    }

    private void ResolveTopLevelVariableDeclaration(VariableDeclaration declaration, Scope scope, NamespaceTrieNode containingNamespace,
                                                    FunctionSymbol? topLevelMain, Scope topLevelMainScope)
    {
        var flags = ResolveVariableFlags(declaration.Modifiers);
        Scope ownerScope = containingNamespace == context.GlobalNamespace ? context.GlobalScope : scope;

        if (topLevelMain is null)
        {
            var declarator = declaration.Declarator;
            if (declarator.Identifier is TupleName tupleName)
            {
                foreach (var elem in tupleName.Elements)
                {
                    var symbol = context.CreateGlobalVariableSymbol(ownerScope, ResolutionContext.GetSymbolName(elem).Last, containingNamespace, declaration);
                    symbol.Flags = flags;
                }
            }
            else
            {
                var symbol = context.CreateGlobalVariableSymbol(ownerScope, ResolutionContext.GetSymbolName(declarator.Identifier).Last, containingNamespace, declaration);
                symbol.Flags = flags;
            }

            return;
        }

        var topDeclarator = declaration.Declarator;
        if (topDeclarator.Identifier is TupleName topTupleName)
        {
            foreach (var elem in topTupleName.Elements)
            {
                LocalVariableSymbol symbol = context.CreateLocalVariableSymbol(topLevelMainScope, context.GetScopedSymbolName(elem).Last,
                                                                             ResolutionContext.GetHandle(topLevelMain), declaration);
                symbol.Flags = flags;
            }
        }
        else
        {
            LocalVariableSymbol symbol = context.CreateLocalVariableSymbol(topLevelMainScope, context.GetScopedSymbolName(topDeclarator.Identifier).Last,
                                                                         ResolutionContext.GetHandle(topLevelMain), declaration);
            symbol.Flags = flags;
        }
    }

    private AliasSymbol ResolveTopLevelAliasDeclaration(AliasDeclaration declaration, Scope enclosingScope, NamespaceTrieNode containingNamespace)
    {
        Scope ownerScope = containingNamespace == context.GlobalNamespace ? context.GlobalScope : enclosingScope;
        var aliasScope = context.CreateScope(enclosingScope);
        var symbol = context.CreateAliasSymbol(ownerScope, ResolutionContext.GetSymbolName(declaration.Name).Last, containingNamespace, declaration);
        ResolutionContext.BindChildScope(ownerScope, symbol, aliasScope);
        context.RegisterSyntaxScope(declaration, aliasScope);
        symbol.GenericParameters = ResolveGenericParameters(declaration.Name, aliasScope, symbol);

        return symbol;
    }

    private void ResolveMemberAliasDeclaration(AliasDeclaration declaration, Scope enclosingScope, SymbolHandle? containingType)
    {
        var aliasScope = context.CreateScope(enclosingScope);
        var symbol = context.CreateAliasSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name).Last, containingType, declaration);
        ResolutionContext.BindChildScope(enclosingScope, symbol, aliasScope);
        context.RegisterSyntaxScope(declaration, aliasScope);
        symbol.GenericParameters = ResolveGenericParameters(declaration.Name, aliasScope, symbol);
    }

    private void ResolveLocalAliasDeclaration(AliasDeclaration declaration, Scope enclosingScope, SymbolHandle? containingSymbol)
    {
        var aliasScope = context.CreateScope(enclosingScope);
        var symbol = context.CreateAliasSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name).Last, containingSymbol, declaration);
        ResolutionContext.BindChildScope(enclosingScope, symbol, aliasScope);
        context.RegisterSyntaxScope(declaration, aliasScope);
        symbol.GenericParameters = ResolveGenericParameters(declaration.Name, aliasScope, symbol);
    }

    private void ResolveTopLevelMacroDeclaration(MacroDeclaration declaration, Scope enclosingScope, NamespaceTrieNode containingNamespace)
    {
        Scope ownerScope = containingNamespace == context.GlobalNamespace ? context.GlobalScope : enclosingScope;
        context.CreateMacroSymbol(ownerScope, new SymbolPart(declaration.Name), containingNamespace, declaration);
        context.RegisterSyntaxScope(declaration, enclosingScope);
    }

    private void ResolveMemberMacroDeclaration(MacroDeclaration declaration, Scope enclosingScope, SymbolHandle? containingType)
    {
        context.CreateMacroSymbol(enclosingScope, new SymbolPart(declaration.Name), containingType, declaration);
        context.RegisterSyntaxScope(declaration, enclosingScope);
    }

    private void ResolveLocalMacroDeclaration(MacroDeclaration declaration, Scope enclosingScope, SymbolHandle? containingMethod)
    {
        context.CreateMacroSymbol(enclosingScope, new SymbolPart(declaration.Name), containingMethod, declaration);
        context.RegisterSyntaxScope(declaration, enclosingScope);
    }

    private void ResolveTopLevelAttributeDeclaration(AttributeSignature declaration, Scope enclosingScope, NamespaceTrieNode containingNamespace)
    {
        Scope ownerScope = containingNamespace == context.GlobalNamespace ? context.GlobalScope : enclosingScope;
        var attributeScope = context.CreateScope(enclosingScope);
        var declaredNamespace = GetDeclaredTypeNamespace(declaration.Name, containingNamespace);
        var symbol = context.CreateAttributeSymbol(ownerScope, ResolutionContext.GetSymbolName(declaration.Name).Last, declaredNamespace, declaration);

        symbol.Flags = ResolveAttributeFlags(declaration.Modifiers);

        if (symbol.Name.Text == "_" || enclosingScope.IsSink)
            attributeScope.IsSink = true;

        ResolutionContext.BindChildScope(ownerScope, symbol, attributeScope);
        context.RegisterSyntaxScope(declaration, attributeScope);
        symbol.Parameters = DiscoverParameters(declaration, attributeScope, ResolutionContext.GetHandle(symbol));
    }

    private void ResolveMemberAttributeDeclaration(AttributeSignature declaration, Scope enclosingScope, SymbolHandle? containingType)
    {
        var attributeScope = context.CreateScope(enclosingScope);
        var symbol = context.CreateMemberAttributeSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name).Last, containingType, declaration);

        symbol.Flags = ResolveAttributeFlags(declaration.Modifiers);

        if (symbol.Name.Text == "_" || enclosingScope.IsSink)
            attributeScope.IsSink = true;

        ResolutionContext.BindChildScope(enclosingScope, symbol, attributeScope);
        context.RegisterSyntaxScope(declaration, attributeScope);
        symbol.Parameters = DiscoverParameters(declaration, attributeScope, ResolutionContext.GetHandle(symbol));
    }

    private LocalAttributeSymbol ResolveLocalAttributeDeclaration(AttributeSignature declaration, Scope enclosingScope, SymbolHandle? containingFunction)
    {
        var attributeScope = context.CreateScope(enclosingScope);
        var symbol = context.CreateLocalAttributeSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name).Last, containingFunction, declaration);

        symbol.Flags = ResolveAttributeFlags(declaration.Modifiers);

        if (symbol.Name.Text == "_" || enclosingScope.IsSink)
            attributeScope.IsSink = true;

        ResolutionContext.BindChildScope(enclosingScope, symbol, attributeScope);
        context.RegisterSyntaxScope(declaration, attributeScope);
        symbol.Parameters = DiscoverParameters(declaration, attributeScope, ResolutionContext.GetHandle(symbol));

        return symbol;
    }

    private void ResolveTopLevelTypeDeclaration(TypeDeclaration declaration, Scope enclosingScope, NamespaceTrieNode containingNamespace)
    {
        Scope ownerScope = containingNamespace == context.GlobalNamespace ? context.GlobalScope : enclosingScope;
        var typeScope = context.CreateScope(enclosingScope);
        var declaredNamespace = GetDeclaredTypeNamespace(declaration.Name, containingNamespace);
        var symbol = context.CreateTypeSymbol(ownerScope, ResolutionContext.GetSymbolName(declaration.Name).Last, ToResolutionTypeKind(declaration.Kind),
                                                      declaredNamespace, declaration);
        if (symbol.Name.Text == "_" || enclosingScope.IsSink)
            typeScope.IsSink = true;

        symbol.Flags = ResolveTypeFlags(declaration.Modifiers);
        if (symbol is ISumTypeSymbol sumSymbol)
            sumSymbol.IsUntagged = symbol.Flags.HasFlag(TypeFlags.Untagged);
        ResolutionContext.BindChildScope(ownerScope, symbol, typeScope);
        context.RegisterSyntaxScope(declaration.Body, typeScope);

        symbol.GenericParameters = ResolveGenericParameters(declaration.Name, typeScope, symbol);

        ResolveTypeBody(declaration.Body, typeScope, ResolutionContext.GetHandle(symbol));
    }

    private void ResolveMemberTypeDeclaration(TypeDeclaration declaration, Scope enclosingScope, SymbolHandle containingType)
    {
        var typeScope = context.CreateScope(enclosingScope);
        var symbol = context.CreateMemberTypeSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name).Last, ToResolutionTypeKind(declaration.Kind),
                                                                               containingType, declaration);
        if (symbol.Name.Text == "_" || enclosingScope.IsSink)
            typeScope.IsSink = true;

        symbol.Flags = ResolveTypeFlags(declaration.Modifiers);
        if (symbol is ISumTypeSymbol sumSymbol)
            sumSymbol.IsUntagged = symbol.Flags.HasFlag(TypeFlags.Untagged);
        ResolutionContext.BindChildScope(enclosingScope, symbol, typeScope);
        context.RegisterSyntaxScope(declaration.Body, typeScope);

        symbol.GenericParameters = ResolveGenericParameters(declaration.Name, typeScope, symbol);
        ResolveTypeBody(declaration.Body, typeScope, ResolutionContext.GetHandle(symbol));
    }

    private LocalTypeSymbol ResolveLocalTypeDeclaration(TypeDeclaration declaration, Scope enclosingScope, SymbolHandle? containingMethod)
    {
        var typeScope = context.CreateScope(enclosingScope);
        var symbol = context.CreateLocalTypeSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name).Last, ToResolutionTypeKind(declaration.Kind),
                                                                containingMethod, declaration);
        if (symbol.Name.Text == "_" || enclosingScope.IsSink)
            typeScope.IsSink = true;

        symbol.Flags = ResolveTypeFlags(declaration.Modifiers);
        if (symbol is ISumTypeSymbol sumSymbol)
            sumSymbol.IsUntagged = symbol.Flags.HasFlag(TypeFlags.Untagged);
        ResolutionContext.BindChildScope(enclosingScope, symbol, typeScope);
        context.RegisterSyntaxScope(declaration.Body, typeScope);

        symbol.GenericParameters = ResolveGenericParameters(declaration.Name, typeScope, symbol);
        ResolveTypeBody(declaration.Body, typeScope, ResolutionContext.GetHandle(symbol));

        return symbol;
    }

    private void ResolveTypeBody(TypeBody body, Scope scope, SymbolHandle containingType)
    {
        if (body is TypeBlockBody block)
        {
            foreach (var member in block.Members)
                ResolveMember(member, scope, containingType);
        }
        else if (body is TypeEnumBody enumBody)
        {
            var sumType = context.GetSymbol(containingType) as ISumTypeSymbol;
            foreach (var variant in enumBody.Variants)
            {
                var variantName = ResolutionContext.GetSymbolName(variant.Name).Last;
                var variantSymbol = context.CreateEnumVariantSymbol(scope, variantName, variant.Kind, containingType, variant, variant.ExplicitValue);
                var variantHandle = ResolutionContext.GetHandle(variantSymbol);
                sumType?.EnumVariants.Add(variantHandle);

                var variantScope = context.CreateScope(scope);
                ResolutionContext.BindChildScope(scope, variantSymbol, variantScope);
                context.RegisterSyntaxScope(variant, variantScope);

                if (variant is EnumTupleVariant tuple)
                {
                    variantSymbol.Parameters = DiscoverParameters(tuple.Parameters, variantScope, variantHandle);
                }
                else if (variant is EnumStructVariant structVar)
                {
                    foreach (var memberField in structVar.Fields)
                    {
                        ResolveFieldDeclaration(memberField.Declaration, variantScope, variantHandle);
                    }
                    foreach (var s in variantScope.Symbols.Values)
                    {
                        if (s.Kind == SymbolKind.Field)
                            variantSymbol.Fields.Add(ResolutionContext.GetHandle(s));
                    }
                }
            }

            foreach (var member in enumBody.Members)
                ResolveMember(member, scope, containingType);
        }
    }

    private void ResolveMember(Member member, Scope scope, SymbolHandle containingType)
    {
        switch (member)
        {
            case MemberBlockDeclaration block:
                {
                    Scope blockScope = context.CreateScope(scope);
                    context.RegisterSyntaxScope(block, blockScope);
                    foreach (var child in block.Members)
                        ResolveMember(child, blockScope, containingType);
                    break;
                }
            case MemberAliasDeclaration declaration:
                ResolveMemberAliasDeclaration(declaration.Alias, scope, containingType);
                break;
            case MemberLiteralMacroDeclaration:
                break;
            case MemberMacroDeclaration declaration:
                ResolveMemberMacroDeclaration(declaration.Macro, scope, containingType);
                break;
            case MemberUsingDirective directive:
                ResolveUsingDirective(directive.Directive, scope);
                break;
            case MemberAttributeDeclaration declaration:
                ResolveMemberAttributeDeclaration(declaration.Attribute, scope, containingType);
                break;
            case MemberTypeDeclaration declaration:
                ResolveMemberTypeDeclaration(declaration.Type, scope, containingType);
                break;
            case MemberFunctionDeclaration declaration:
                ResolveMemberFunctionDeclaration(declaration.Function, scope, containingType);
                break;
            case MemberFieldDeclaration declaration:
                ResolveFieldDeclaration(declaration.Declaration, scope, containingType);
                break;
            case MemberPropertyDeclaration declaration:
                ResolvePropertyDeclaration(declaration, scope, containingType);
                break;
        }
    }

    private void ResolveTopLevelFunctionDeclaration(FunctionDeclaration declaration, Scope enclosingScope, NamespaceTrieNode containingNamespace)
    {
        Scope ownerScope = containingNamespace == context.GlobalNamespace ? context.GlobalScope : enclosingScope;
        var functionScope = context.CreateScope(enclosingScope);
        var symbolName = GetSpecialFunctionSymbolName(declaration);
        var symbol = context.CreateFunctionSymbol(ownerScope, symbolName, containingNamespace, declaration);
        symbol.SpecialKind = declaration.SpecialKind;
        if (symbol.Name.Text == "_" || enclosingScope.IsSink)
            functionScope.IsSink = true;

        ResolutionContext.BindChildScope(ownerScope, symbol, functionScope);
        context.RegisterSyntaxScope(declaration.Body, functionScope);

        symbol.Flags = ResolveFunctionFlags(declaration.Signature.Modifiers);

        symbol.GenericParameters = ResolveGenericParameters(declaration.Signature.Identifier, functionScope, symbol);
        symbol.Parameters = DiscoverParameters(declaration.Signature, functionScope, ResolutionContext.GetHandle(symbol));
        ResolveFunctionBody(declaration.Body, functionScope, ResolutionContext.GetHandle(symbol), containingMethod: null);
    }

}
