using System;
using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class SymbolDiscoveryPass : ResolutionPass
{
    private ResolutionContext context = null!;

    public override void Resolve(ResolutionContext context)
    {
        this.context = context;

        foreach (var root in context.SyntaxTree.Roots)
            ResolveCompilationUnit(root);
    }

    private void ResolveCompilationUnit(CompilationUnit unit)
    {
        FunctionSymbol? topLevelMain = null;
        var topLevelMainScope = context.GlobalScope;

        if (PragmaDirective.EnablesTopLevelStatements(unit.Pragmas))
        {
            topLevelMainScope = context.CreateScope(context.GlobalScope);
            topLevelMain = context.CreateFunctionSymbol(context.GlobalScope, new SymbolPart("Main"), context.GlobalNamespace, syntax: null);
            ResolutionContext.BindChildScope(context.GlobalScope, topLevelMain, topLevelMainScope);
            context.RegisterSyntaxScope(unit, topLevelMainScope);
        }

        ResolveTopLevelScope(unit.Members, context.GlobalScope, context.GlobalNamespace, topLevelMain, topLevelMainScope);
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
                ResolveTopLevelScope(block.Members, scope, containingNamespace, HasGlobalModifier(block) ? null : topLevelMain, topLevelMainScope);
                return containingNamespace;
            case TopLevelAttributeDeclaration declaration:
                ResolveTopLevelAttributeDeclaration(declaration.Attribute, scope, containingNamespace);
                return containingNamespace;
            case TopLevelTypeDeclaration declaration:
                ResolveTopLevelTypeDeclaration(declaration.Type, scope, containingNamespace);
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
            case TopLevelIfStatement conditional:
                DiscoverTopLevelLabels(conditional.ThenStatement, scope, containingFunction);
                if (conditional.ElseStatement is not null)
                    DiscoverTopLevelLabels(conditional.ElseStatement.Statement, scope, containingFunction);
                break;
            case TopLevelWhileStatement loop:
                DiscoverTopLevelLabels(loop.Statement, scope, containingFunction);
                break;
        }
    }

    private NamespaceTrieNode ResolveNamespaceDeclaration(NamespaceDeclaration declaration, Scope scope, NamespaceTrieNode containingNamespace,
                                                           FunctionSymbol? topLevelMain, Scope topLevelMainScope)
    {
        var declaredNamespace = GetOrDeclareNamespace(containingNamespace, declaration.Name);

        if (declaration.Body is NamespaceEmptyBody)
            return declaredNamespace;

        var body = (NamespaceBlockBody)declaration.Body;
        ResolveTopLevelScope(body.Members, scope, declaredNamespace, topLevelMain, topLevelMainScope);
        return containingNamespace;
    }

    private void ResolveTopLevelVariableDeclaration(VariableDeclaration declaration, Scope scope, NamespaceTrieNode containingNamespace,
                                                    FunctionSymbol? topLevelMain, Scope topLevelMainScope)
    {
        var flags = ResolveVariableFlags(declaration.Modifiers);

        if (topLevelMain is null)
        {
            foreach (var declarator in declaration.Declarators)
            {
                var symbol = context.CreateGlobalVariableSymbol(scope, ResolutionContext.GetSymbolName(declarator.Identifier).Last, containingNamespace, declaration);
                symbol.Flags = flags;
            }

            return;
        }

        foreach (var declarator in declaration.Declarators)
        {
            LocalVariableSymbol symbol = context.CreateLocalVariableSymbol(topLevelMainScope, ResolutionContext.GetSymbolName(declarator.Identifier).Last,
                                                                         ResolutionContext.GetHandle(topLevelMain), declaration);

            symbol.Flags = flags;

            topLevelMain.LocalVariables.Add(ResolutionContext.GetHandle(symbol));
        }
    }

    private void ResolveTopLevelAliasDeclaration(AliasDeclaration declaration, Scope enclosingScope, NamespaceTrieNode containingNamespace)
    {
        var aliasScope = context.CreateScope(enclosingScope);
        var symbol = context.CreateAliasSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name).Last, containingNamespace, declaration);
        ResolutionContext.BindChildScope(enclosingScope, symbol, aliasScope);
        context.RegisterSyntaxScope(declaration, aliasScope);
        symbol.GenericParameters = ResolveGenericParameters(declaration.Name, aliasScope, symbol);
    }

    private void ResolveTopLevelAttributeDeclaration(AttributeSignature declaration, Scope enclosingScope, NamespaceTrieNode containingNamespace)
    {
        var attributeScope = context.CreateScope(enclosingScope);
        var declaredNamespace = GetDeclaredTypeNamespace(declaration.Name, containingNamespace);
        var symbol = context.CreateAttributeSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name).Last, declaredNamespace, declaration);

        symbol.Flags = ResolveAttributeFlags(declaration.Modifiers);
        ResolutionContext.BindChildScope(enclosingScope, symbol, attributeScope);
        context.RegisterSyntaxScope(declaration, attributeScope);
        symbol.Parameters = DiscoverParameters(declaration, attributeScope, ResolutionContext.GetHandle(symbol));
    }

    private void ResolveMemberAttributeDeclaration(AttributeSignature declaration, Scope enclosingScope, SymbolHandle? containingType)
    {
        var attributeScope = context.CreateScope(enclosingScope);
        var symbol = context.CreateMemberAttributeSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name).Last, containingType, declaration);

        symbol.Flags = ResolveAttributeFlags(declaration.Modifiers);
        ResolutionContext.BindChildScope(enclosingScope, symbol, attributeScope);
        context.RegisterSyntaxScope(declaration, attributeScope);
        symbol.Parameters = DiscoverParameters(declaration, attributeScope, ResolutionContext.GetHandle(symbol));
    }

    private LocalAttributeSymbol ResolveLocalAttributeDeclaration(AttributeSignature declaration, Scope enclosingScope, SymbolHandle? containingFunction)
    {
        var attributeScope = context.CreateScope(enclosingScope);
        var symbol = context.CreateLocalAttributeSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name).Last, containingFunction, declaration);

        symbol.Flags = ResolveAttributeFlags(declaration.Modifiers);
        ResolutionContext.BindChildScope(enclosingScope, symbol, attributeScope);
        context.RegisterSyntaxScope(declaration, attributeScope);
        symbol.Parameters = DiscoverParameters(declaration, attributeScope, ResolutionContext.GetHandle(symbol));

        return symbol;
    }

    private void ResolveTopLevelTypeDeclaration(TypeDeclaration declaration, Scope enclosingScope, NamespaceTrieNode containingNamespace)
    {
        var typeScope = context.CreateScope(enclosingScope);
        var declaredNamespace = GetDeclaredTypeNamespace(declaration.Name, containingNamespace);
        var symbol = context.CreateTypeSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name).Last, ToResolutionTypeKind(declaration.Kind),
                                                      declaredNamespace, declaration);

        symbol.Flags = ResolveTypeFlags(declaration.Modifiers);
        ResolutionContext.BindChildScope(enclosingScope, symbol, typeScope);
        context.RegisterSyntaxScope(declaration.Body, typeScope);

        symbol.GenericParameters = ResolveGenericParameters(declaration.Name, typeScope, symbol);

        ResolveTypeBody(declaration.Body, typeScope, ResolutionContext.GetHandle(symbol));
    }

    private void ResolveMemberTypeDeclaration(TypeDeclaration declaration, Scope enclosingScope, SymbolHandle containingType)
    {
        var typeScope = context.CreateScope(enclosingScope);
        var symbol = context.CreateMemberTypeSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name).Last, ToResolutionTypeKind(declaration.Kind),
                                                                               containingType, declaration);

        symbol.Flags = ResolveTypeFlags(declaration.Modifiers);
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

        symbol.Flags = ResolveTypeFlags(declaration.Modifiers);
        ResolutionContext.BindChildScope(enclosingScope, symbol, typeScope);
        context.RegisterSyntaxScope(declaration.Body, typeScope);

        symbol.GenericParameters = ResolveGenericParameters(declaration.Name, typeScope, symbol);
        ResolveTypeBody(declaration.Body, typeScope, ResolutionContext.GetHandle(symbol));

        return symbol;
    }

    private void ResolveTypeBody(TypeBody body, Scope scope, SymbolHandle containingType)
    {
        if (body is not TypeBlockBody block)
            return;

        foreach (var member in block.Members)
            ResolveMember(member, scope, containingType);
    }

    private void ResolveMember(Member member, Scope scope, SymbolHandle containingType)
    {
        switch (member)
        {
            case MemberBlockDeclaration block:
                foreach (var child in block.Members)
                    ResolveMember(child, scope, containingType);
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
                ResolvePropertyDeclaration(declaration, scope);
                break;
        }
    }

    private void ResolveTopLevelFunctionDeclaration(FunctionDeclaration declaration, Scope enclosingScope, NamespaceTrieNode containingNamespace)
    {
        var functionScope = context.CreateScope(enclosingScope);
        var symbol = context.CreateFunctionSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Signature.Identifier).Last, containingNamespace,
                                                              declaration);
        ResolutionContext.BindChildScope(enclosingScope, symbol, functionScope);
        context.RegisterSyntaxScope(declaration.Body, functionScope);

        symbol.GenericParameters = ResolveGenericParameters(declaration.Signature.Identifier, functionScope, symbol);
        symbol.Parameters = DiscoverParameters(declaration.Signature, functionScope, ResolutionContext.GetHandle(symbol));
        ResolveFunctionBody(declaration.Body, functionScope, ResolutionContext.GetHandle(symbol), containingMethod: null);
    }

    private void ResolveMemberFunctionDeclaration(FunctionDeclaration declaration, Scope enclosingScope, SymbolHandle containingType)
    {
        var functionScope = context.CreateScope(enclosingScope);
        var symbol = context.CreateMemberMethodSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Signature.Identifier).Last, containingType, declaration);
        ResolutionContext.BindChildScope(enclosingScope, symbol, functionScope);
        context.RegisterSyntaxScope(declaration.Body, functionScope);

        symbol.GenericParameters = ResolveGenericParameters(declaration.Signature.Identifier, functionScope, symbol);

        var handle = ResolutionContext.GetHandle(symbol);

        symbol.Parameters = DiscoverParameters(declaration.Signature, functionScope, handle);
        ResolveFunctionBody(declaration.Body, functionScope, handle, handle);
    }

    private LocalFunctionSymbol ResolveLocalFunctionDeclaration(FunctionDeclaration declaration, Scope enclosingScope, SymbolHandle? containingMethod)
    {
        var functionScope = context.CreateScope(enclosingScope);
        var symbol = context.CreateLocalFunctionSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Signature.Identifier).Last, containingMethod, declaration);
        ResolutionContext.BindChildScope(enclosingScope, symbol, functionScope);
        context.RegisterSyntaxScope(declaration.Body, functionScope);

        symbol.GenericParameters = ResolveGenericParameters(declaration.Signature.Identifier, functionScope, symbol);

        var handle = ResolutionContext.GetHandle(symbol);

        symbol.Parameters = DiscoverParameters(declaration.Signature, functionScope, handle);
        ResolveFunctionBody(declaration.Body, functionScope, handle, handle);

        return symbol;
    }

    private void ResolvePropertyDeclaration(MemberPropertyDeclaration declaration, Scope enclosingScope)
    {
        var symbol = context.CreatePropertySymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Identifier).Last, hasBacking: false, declaration);

        foreach (var accessor in declaration.Body.Accessors)
        {
            Scope accessorScope = context.CreateScope(enclosingScope);
            context.RegisterSyntaxScope(accessor.Body, accessorScope);
            ResolveFunctionBody(accessor.Body, accessorScope, ResolutionContext.GetHandle(symbol), containingMethod: null);
        }
    }

    private List<SymbolHandle> DiscoverParameters(AttributeSignature signature, Scope scope, SymbolHandle containingAttribute)
    {
        if (signature.Parameters is not null)
        {
            var list = new List<SymbolHandle>(signature.Parameters.Parameters.Count);

            foreach (var param in signature.Parameters.Parameters)
            {
                var symbol = context.CreateParameterSymbol(scope, ResolutionContext.GetSymbolName(param.Declarator.Identifier).Last, containingAttribute, param);
                list.Add(ResolutionContext.GetHandle(symbol));
            }

            return list;
        }

        return [];
    }

    private List<SymbolHandle> DiscoverParameters(FunctionSignature signature, Scope scope, SymbolHandle containingFunction)
    {
        var list = new List<SymbolHandle>(signature.Parameters.Count);
        foreach (var param in signature.Parameters)
        {
            var symbol = context.CreateParameterSymbol(scope, ResolutionContext.GetSymbolName(param.Declarator.Identifier).Last, containingFunction, param);
            list.Add(ResolutionContext.GetHandle(symbol));
        }

        return list;
    }


    private void ResolveFunctionBody(FunctionBody body, Scope scope, SymbolHandle containingSymbol, SymbolHandle? containingMethod)
    {
        if (body is not FunctionBlockBody block)
            return;

        foreach (var local in block.Locals)
            ResolveLocal(local, scope, containingSymbol, containingMethod);
    }

    private void ResolveFieldDeclaration(VariableDeclaration declaration, Scope scope, SymbolHandle containingType)
    {
        foreach (var declarator in declaration.Declarators)
        {
            var symbol = context.CreateFieldSymbol(scope, ResolutionContext.GetSymbolName(declarator.Identifier).Last, containingType, declaration);
            symbol.Flags = ResolveVariableFlags(declaration.Modifiers);
        }
    }

    private List<LocalVariableSymbol> ResolveLocalVariableDeclaration(VariableDeclaration declaration, Scope scope, SymbolHandle containingSymbol)
    {
        var symbols = new List<LocalVariableSymbol>(declaration.Declarators.Count);
        foreach (var declarator in declaration.Declarators)
        {
            var symbol = context.CreateLocalVariableSymbol(scope, ResolutionContext.GetSymbolName(declarator.Identifier).Last, containingSymbol, declaration);
            symbol.Flags = ResolveVariableFlags(declaration.Modifiers);
            symbols.Add(symbol);
        }

        return symbols;
    }

    private void ResolveLocal(Local local, Scope scope, SymbolHandle containingSymbol, SymbolHandle? containingMethod)
    {
        switch (local)
        {
            case LocalBlockStatement block:
                {
                    Scope blockScope = context.CreateScope(scope);
                    context.RegisterSyntaxScope(block, blockScope);

                    foreach (var child in block.Locals)
                        ResolveLocal(child, blockScope, containingSymbol, containingMethod);
                    break;
                }
            case LocalLabelStatement label:
                context.CreateLabelSymbol(scope, new SymbolPart(label.Identifier), containingSymbol, label);
                break;
            case LocalAttributeDeclaration declaration:
                RegisterLocalAttribute(containingSymbol, ResolveLocalAttributeDeclaration(declaration.Attribute, scope, containingMethod));
                break;
            case LocalTypeDeclaration declaration:
                RegisterLocalType(containingSymbol, ResolveLocalTypeDeclaration(declaration.Type, scope, containingMethod));
                break;
            case LocalFunctionDeclaration declaration:
                RegisterLocalFunction(containingSymbol, ResolveLocalFunctionDeclaration(declaration.Function, scope, containingMethod));
                break;
            case LocalVariableDeclarationStatement declaration:
                foreach (var variable in ResolveLocalVariableDeclaration(declaration.Declaration, scope, containingSymbol))
                    RegisterLocalVariable(containingSymbol, variable);
                break;
        }
    }

    private void RegisterLocalVariable(SymbolHandle owner, LocalVariableSymbol local)
    {
        if (owner.Kind is SymbolKind.Function)
            context.FunctionSymbols[owner.ID].LocalVariables.Add(ResolutionContext.GetHandle(local));
        else if (owner.Kind is SymbolKind.Method)
            context.MethodSymbols[owner.ID].LocalVariables.Add(ResolutionContext.GetHandle(local));
    }

    private void RegisterLocalFunction(SymbolHandle owner, LocalFunctionSymbol local)
    {
        if (owner.Kind is SymbolKind.Function)
            context.FunctionSymbols[owner.ID].LocalFunctions.Add(ResolutionContext.GetHandle(local));
        else if (owner.Kind is SymbolKind.Method)
            context.MethodSymbols[owner.ID].LocalFunctions.Add(ResolutionContext.GetHandle(local));
    }

    private void RegisterLocalAttribute(SymbolHandle owner, LocalAttributeSymbol local)
    {
        if (owner.Kind is SymbolKind.Function)
            context.FunctionSymbols[owner.ID].LocalFunctions.Add(ResolutionContext.GetHandle(local));
        else if (owner.Kind is SymbolKind.Method)
            context.MethodSymbols[owner.ID].LocalFunctions.Add(ResolutionContext.GetHandle(local));
    }

    private void RegisterLocalType(SymbolHandle owner, LocalTypeSymbol local)
    {
        if (owner.Kind is SymbolKind.Function)
            context.FunctionSymbols[owner.ID].LocalTypes.Add(ResolutionContext.GetHandle(local));
        else if (owner.Kind is SymbolKind.Method)
            context.MethodSymbols[owner.ID].LocalTypes.Add(ResolutionContext.GetHandle(local));
    }

    private List<SymbolHandle> ResolveGenericParameters(NamedSyntax name, Scope scope, Symbol genericSymbol)
    {
        var genericName = GetGenericName(name);

        if (genericName is null)
            return [];

        var genericParameters = new List<SymbolHandle>(genericName.GenericParameters.Count);

        foreach (var genericParameter in genericName.GenericParameters)
        {
            var symbol = context.CreateGenericParameterSymbol(scope, new SymbolPart(genericParameter.Identifier), genericSymbol, genericParameter.Kind, genericParameter.IsVariadic);
            genericParameters.Add((symbol.Kind, symbol.ID));
        }

        return genericParameters;
    }

    private static NamespaceTrieNode GetOrDeclareNamespace(NamespaceTrieNode containingNamespace, NamedSyntax name)
    {
        return name switch
        {
            SimpleName simpleName => ResolutionContext.GetOrDeclareNamespace(containingNamespace, new SymbolPart(simpleName.Name)),
            GenericName => containingNamespace,
            QualifiedName qualifiedName => GetOrDeclareQualifiedNamespace(containingNamespace, qualifiedName),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    private NamespaceTrieNode GetDeclaredTypeNamespace(NamedSyntax name, NamespaceTrieNode containingNamespace)
    {
        if (name is not QualifiedName qualifiedName || qualifiedName.Parts.Count < 2)
            return containingNamespace;

        NamespaceTrieNode current = context.GlobalNamespace;

        for (int index = 0; index < qualifiedName.Parts.Count - 1; index++)
            current = GetOrDeclareNamespace(current, qualifiedName.Parts[index]);

        return current;
    }

    private static NamespaceTrieNode GetOrDeclareQualifiedNamespace(NamespaceTrieNode containingNamespace, QualifiedName name)
    {
        NamespaceTrieNode current = containingNamespace;

        foreach (var part in name.Parts)
            current = GetOrDeclareNamespace(current, part);

        return current;
    }

    private static GenericName? GetGenericName(NamedSyntax name)
    {
        return name switch
        {
            GenericName genericName => genericName,
            QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetGenericName(qualifiedName.Parts[^1]),
            _ => null
        };
    }

    private static TypeKind ToResolutionTypeKind(Maho.TypeKind kind)
    {
        return kind switch
        {
            Maho.TypeKind.Struct => TypeKind.Struct,
            Maho.TypeKind.Class => TypeKind.Class,
            Maho.TypeKind.Interface => TypeKind.Interface,
            Maho.TypeKind.Attribute => TypeKind.Attribute,
            Maho.TypeKind.Union => TypeKind.Union,
            Maho.TypeKind.Enum => TypeKind.Enum,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    // TODO: Define order in which modifiers should appear and add diagnostic otherwise.
    private static TypeFlags ResolveTypeFlags(IReadOnlyList<Token> modifiers)
    {
        var flags = TypeFlags.None;

        foreach (var mod in modifiers)
        {
            flags |= mod.MatchingKind switch
            {
                MatchingKeywordKind.Public => TypeFlags.Public,
                MatchingKeywordKind.Internal => TypeFlags.Internal,
                MatchingKeywordKind.Unsafe => TypeFlags.Unsafe,
                MatchingKeywordKind.Unsealed => ~TypeFlags.Sealed,
                MatchingKeywordKind.Static => TypeFlags.Static,
                _ => TypeFlags.None
            };
        }

        return flags;
    }

    private static AttributeFlags ResolveAttributeFlags(IReadOnlyList<Token> modifiers)
    {
        var flags = AttributeFlags.None;

        foreach (var mod in modifiers)
        {
            flags |= mod.MatchingKind switch
            {
                MatchingKeywordKind.Public => AttributeFlags.Public,
                MatchingKeywordKind.Internal => AttributeFlags.Internal,
                MatchingKeywordKind.Intrinsic => AttributeFlags.Intrinsic,
                _ => AttributeFlags.None
            };
        }

        return flags;
    }

    private static VariableFlags ResolveVariableFlags(IReadOnlyList<Token> modifiers)
    {
        var flags = VariableFlags.None;

        foreach (var mod in modifiers)
        {
            flags |= mod.MatchingKind switch
            {
                MatchingKeywordKind.Public => VariableFlags.Public,
                MatchingKeywordKind.Internal => VariableFlags.Internal,
                MatchingKeywordKind.Static => VariableFlags.Static,
                MatchingKeywordKind.Protected => VariableFlags.Protected,
                MatchingKeywordKind.Const => VariableFlags.Const,
                _ => VariableFlags.None
            };
        }

        return flags;
    }
}
