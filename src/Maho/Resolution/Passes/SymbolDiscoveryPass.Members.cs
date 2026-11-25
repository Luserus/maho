using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed partial class SymbolDiscoveryPass
{
    private void ResolveMemberFunctionDeclaration(FunctionDeclaration declaration, Scope enclosingScope, SymbolHandle containingType)
    {
        var functionScope = context.CreateScope(enclosingScope);
        var symbolName = GetSpecialFunctionSymbolName(declaration);
        var symbol = context.CreateMemberMethodSymbol(enclosingScope, symbolName, containingType, declaration);
        symbol.SpecialKind = declaration.SpecialKind;
        if (symbol.Name.Text == "_" || enclosingScope.IsSink)
            functionScope.IsSink = true;

        ResolutionContext.BindChildScope(enclosingScope, symbol, functionScope);
        context.RegisterSyntaxScope(declaration.Body, functionScope);

        symbol.Flags = ResolveFunctionFlags(declaration.Signature.Modifiers);
        bool hasReceiver = declaration.Signature.Parameters.Count > 0 && declaration.Signature.Parameters[0].Declarator.IsReceiver;
        if (hasReceiver)
            symbol.Flags &= ~FunctionFlags.Static;
        else
            // A member without an explicit first `this` parameter cannot bind instance storage.
            // It is static by semantic shape; the `static` modifier remains accepted but optional.
            symbol.Flags |= FunctionFlags.Static;

        if (hasReceiver && declaration.Signature.Parameters[0].Declarator.Modifiers.Any(m => m.MatchingKind == MatchingKeywordKind.Untagged))
        {
            symbol.Flags |= FunctionFlags.Untagged;
        }

        if (declaration.Signature.Identifier is QualifiedName qual && qual.Parts.Count >= 2)
        {
            symbol.Flags |= FunctionFlags.InterfaceScoped;
            var ifaceParts = new SymbolPart[qual.Parts.Count - 1];
            for (int i = 0; i < ifaceParts.Length; i++)
                ifaceParts[i] = ResolutionContext.GetSymbolName(qual.Parts[i]).Last;
            symbol.ExplicitInterfaceName = new SymbolName(ifaceParts);
        }

        symbol.GenericParameters = ResolveGenericParameters(declaration.Signature.Identifier, functionScope, symbol);

        var handle = ResolutionContext.GetHandle(symbol);

        symbol.Parameters = DiscoverParameters(declaration.Signature, functionScope, handle);
        ResolveFunctionBody(declaration.Body, functionScope, handle, handle);
    }

    private LocalFunctionSymbol ResolveLocalFunctionDeclaration(FunctionDeclaration declaration, Scope enclosingScope, SymbolHandle? containingMethod)
    {
        var functionScope = context.CreateScope(enclosingScope);
        var symbolName = GetSpecialFunctionSymbolName(declaration);
        var symbol = context.CreateLocalFunctionSymbol(enclosingScope, symbolName, containingMethod, declaration);
        symbol.SpecialKind = declaration.SpecialKind;
        if (symbol.Name.Text == "_" || enclosingScope.IsSink)
            functionScope.IsSink = true;

        ResolutionContext.BindChildScope(enclosingScope, symbol, functionScope);
        context.RegisterSyntaxScope(declaration.Body, functionScope);

        symbol.Flags = ResolveFunctionFlags(declaration.Signature.Modifiers);

        symbol.GenericParameters = ResolveGenericParameters(declaration.Signature.Identifier, functionScope, symbol);

        var handle = ResolutionContext.GetHandle(symbol);

        symbol.Parameters = DiscoverParameters(declaration.Signature, functionScope, handle);
        ResolveFunctionBody(declaration.Body, functionScope, handle, handle);

        return symbol;
    }

    private void ResolvePropertyDeclaration(MemberPropertyDeclaration declaration, Scope enclosingScope, SymbolHandle? containingType = null)
    {
        bool hasBacking = declaration.Body.Accessors.Count > 0;
        foreach (var a in declaration.Body.Accessors)
        {
            if (a.Body is not FunctionEmptyBody)
            {
                hasBacking = false;
                break;
            }
        }

        var symbol = context.CreatePropertySymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Identifier).Last, containingType, hasBacking, declaration);
        symbol.Flags = ResolveFunctionFlags(declaration.Modifiers);
        symbol.GetterFlags = symbol.Flags;
        symbol.SetterFlags = symbol.Flags;

        if (declaration.Identifier is QualifiedName qual && qual.Parts.Count >= 2)
        {
            symbol.Flags |= FunctionFlags.InterfaceScoped;
            var ifaceParts = new SymbolPart[qual.Parts.Count - 1];
            for (int i = 0; i < ifaceParts.Length; i++)
                ifaceParts[i] = ResolutionContext.GetSymbolName(qual.Parts[i]).Last;
            symbol.ExplicitInterfaceName = new SymbolName(ifaceParts);
        }

        foreach (var accessor in declaration.Body.Accessors)
        {
            var accFlags = accessor.Modifiers.Count > 0 ? ResolveFunctionFlags(accessor.Modifiers) : symbol.Flags;
            if (accessor.Kind == PropertyAccessorKind.Get)
                symbol.GetterFlags = accFlags;
            else if (accessor.Kind == PropertyAccessorKind.Set)
                symbol.SetterFlags = accFlags;

            Scope accessorScope = context.CreateScope(enclosingScope);
            if (symbol.Name.Text == "_" || enclosingScope.IsSink)
                accessorScope.IsSink = true;

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

    private List<SymbolHandle> DiscoverParameters(FunctionSignature signature, Scope scope, SymbolHandle containingFunction) =>
        DiscoverParameters(signature.Parameters, scope, containingFunction);

    private List<SymbolHandle> DiscoverParameters(SeparatedSyntaxList<Parameter> parameters, Scope scope, SymbolHandle containingSymbol)
    {
        var list = new List<SymbolHandle>(parameters.Count);
        foreach (var param in parameters)
        {
            var symbol = context.CreateParameterSymbol(scope, ResolutionContext.GetSymbolName(param.Declarator.Identifier).Last, containingSymbol, param);
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
        var (dirAcc, extAcc) = ResolveFieldAccessibility(declaration.Modifiers);
        var declarator = declaration.Declarator;
        if (declarator.Identifier is TupleName tupleName)
        {
            foreach (var elem in tupleName.Elements)
            {
                var symbol = context.CreateFieldSymbol(scope, ResolutionContext.GetSymbolName(elem).Last, containingType, declaration);
                symbol.DirectAccessibility = dirAcc;
                symbol.ExtensionAccessibility = extAcc;
                symbol.Flags = ResolveVariableFlags(declaration.Modifiers, dirAcc);
            }
        }
        else
        {
            var symbol = context.CreateFieldSymbol(scope, ResolutionContext.GetSymbolName(declarator.Identifier).Last, containingType, declaration);
            symbol.DirectAccessibility = dirAcc;
            symbol.ExtensionAccessibility = extAcc;
            symbol.Flags = ResolveVariableFlags(declaration.Modifiers, dirAcc);
        }
    }

    private List<LocalVariableSymbol> ResolveLocalVariableDeclaration(VariableDeclaration declaration, Scope scope, SymbolHandle containingSymbol)
    {
        var symbols = new List<LocalVariableSymbol>();
        var declarator = declaration.Declarator;
        if (declarator.Identifier is TupleName tupleName)
        {
            foreach (var elem in tupleName.Elements)
            {
                var symbol = context.CreateLocalVariableSymbol(scope, context.GetScopedSymbolName(elem).Last, containingSymbol, declaration);
                symbol.Flags = ResolveVariableFlags(declaration.Modifiers);
                symbols.Add(symbol);
            }
        }
        else
        {
            var symbol = context.CreateLocalVariableSymbol(scope, context.GetScopedSymbolName(declarator.Identifier).Last, containingSymbol, declaration);
            symbol.Flags = ResolveVariableFlags(declaration.Modifiers);
            symbols.Add(symbol);
        }

        return symbols;
    }

    private void ResolveLocalConstantDeclaration(ConstantDeclaration declaration, Scope scope, SymbolHandle containingSymbol)
    {
        var namePart = context.GetScopedSymbolName(declaration.Identifier).Last;
        var symbol = context.CreateConstantSymbol(scope, namePart, containingNamespace: null, declaration);
        symbol.Parent = containingSymbol;
        symbol.Flags = ResolveVariableFlags(declaration.Modifiers) | VariableFlags.Const;
    }

    private void ResolveLocal(Local local, Scope scope, SymbolHandle containingSymbol, SymbolHandle? containingMethod)
    {
        switch (local)
        {
            case LocalLabelStatement label:
                context.CreateLabelSymbol(scope, new SymbolPart(label.Identifier), containingSymbol, label);
                break;
            case LocalAliasDeclaration declaration:
                ResolveLocalAliasDeclaration(declaration.Alias, scope, containingSymbol);
                break;
            case LocalExternUsingDirective externDirective:
                ResolveExternUsingDirective(externDirective.Directive, scope);
                break;
            case LocalUsingDirective directive:
                ResolveUsingDirective(directive.Directive, scope);
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
            case LocalConstantDeclaration declaration:
                ResolveLocalConstantDeclaration(declaration.Declaration, scope, containingSymbol);
                break;
            case LocalLiteralMacroDeclaration:
                break;
            case LocalMacroDeclaration declaration:
                ResolveLocalMacroDeclaration(declaration.Macro, scope, containingMethod);
                break;
            case LocalVariableDeclarationStatement declaration:
                if (declaration.Declaration.Declarator.Initializer?.Initializer is { } initExpr)
                    DiscoverBlockExpressions(initExpr, scope, containingSymbol, containingMethod);
                ResolveLocalVariableDeclaration(declaration.Declaration, scope, containingSymbol);
                break;
            case LocalReturnStatement retStmt:
                if (retStmt.Statement.Expression is not null)
                    DiscoverBlockExpressions(retStmt.Statement.Expression, scope, containingSymbol, containingMethod);
                break;
            case LocalYieldStatement yieldStmt:
                if (yieldStmt.Expression is not null)
                    DiscoverBlockExpressions(yieldStmt.Expression, scope, containingSymbol, containingMethod);
                break;
            case LocalExpressionStatement exprStmt:
                DiscoverBlockExpressions(exprStmt.Expression, scope, containingSymbol, containingMethod);
                break;
        }
    }

    private void DiscoverBlockExpressions(Expression? expr, Scope scope, SymbolHandle containingSymbol, SymbolHandle? containingMethod)
    {
        if (expr is null)
            return;

        if (expr is BlockExpression block)
        {
            Scope blockScope = context.CreateScope(scope);
            context.RegisterSyntaxScope(block, blockScope);

            foreach (var child in block.Locals)
                ResolveLocal(child, blockScope, containingSymbol, containingMethod);

            DiscoverBlockExpressions(block.FinalExpression, blockScope, containingSymbol, containingMethod);
            return;
        }

        if (expr is BinaryExpression bin)
        {
            DiscoverBlockExpressions(bin.LeftExpression, scope, containingSymbol, containingMethod);
            DiscoverBlockExpressions(bin.RightExpression, scope, containingSymbol, containingMethod);
        }
        else if (expr is ParenthesizedExpression paren)
        {
            DiscoverBlockExpressions(paren.Expression, scope, containingSymbol, containingMethod);
        }
        else if (expr is NameofExpression nameofExpr)
        {
            DiscoverBlockExpressions(nameofExpr.Argument, scope, containingSymbol, containingMethod);
        }
        else if (expr is UnaryExpression unary)
        {
            DiscoverBlockExpressions(unary.Operand, scope, containingSymbol, containingMethod);
        }
        else if (expr is CallExpression call)
        {
            DiscoverBlockExpressions(call.Callee, scope, containingSymbol, containingMethod);
            foreach (var arg in call.Arguments)
                DiscoverBlockExpressions(arg, scope, containingSymbol, containingMethod);
        }
        else if (expr is AssignmentExpression assign)
        {
            DiscoverBlockExpressions(assign.LhsExpression, scope, containingSymbol, containingMethod);
            DiscoverBlockExpressions(assign.RhsExpression, scope, containingSymbol, containingMethod);
        }
        else if (expr is IfExpression ifExpr)
        {
            Scope ifScope = ifExpr.Declaration is not null ? context.CreateScope(scope) : scope;
            if (ifExpr.Declaration is not null)
            {
                context.RegisterSyntaxScope(ifExpr, ifScope);
                if (ifExpr.Declaration.Declarator.Initializer?.Initializer is { } initExpr)
                    DiscoverBlockExpressions(initExpr, ifScope, containingSymbol, containingMethod);
                ResolveLocalVariableDeclaration(ifExpr.Declaration, ifScope, containingSymbol);
            }

            DiscoverBlockExpressions(ifExpr.Condition, ifScope, containingSymbol, containingMethod);
            DiscoverBlockExpressions(ifExpr.ThenExpression, ifScope, containingSymbol, containingMethod);
            if (ifExpr.ElseExpression is not null)
                DiscoverBlockExpressions(ifExpr.ElseExpression.Expression, ifScope, containingSymbol, containingMethod);
        }
        else if (expr is ElseExpression elseExpr)
        {
            DiscoverBlockExpressions(elseExpr.Expression, scope, containingSymbol, containingMethod);
        }
        else if (expr is TupleExpression tuple)
        {
            foreach (var arg in tuple.Arguments)
                DiscoverBlockExpressions(arg, scope, containingSymbol, containingMethod);
        }
        else if (expr is AsExpression asExpr)
        {
            DiscoverBlockExpressions(asExpr.Expression, scope, containingSymbol, containingMethod);
        }
        else if (expr is TypeAsExpression)
        {
        }
        else if (expr is WhileExpression whileExpr)
        {
            Scope whileScope = whileExpr.Declaration is not null ? context.CreateScope(scope) : scope;
            if (whileExpr.Declaration is not null)
            {
                context.RegisterSyntaxScope(whileExpr, whileScope);
                if (whileExpr.Declaration.Declarator.Initializer?.Initializer is { } initExpr)
                    DiscoverBlockExpressions(initExpr, whileScope, containingSymbol, containingMethod);
                ResolveLocalVariableDeclaration(whileExpr.Declaration, whileScope, containingSymbol);
            }

            DiscoverBlockExpressions(whileExpr.Condition, whileScope, containingSymbol, containingMethod);
            DiscoverBlockExpressions(whileExpr.Body, whileScope, containingSymbol, containingMethod);
        }
        else if (expr is ForExpression forExpr)
        {
            var forScope = context.CreateScope(scope);
            context.RegisterSyntaxScope(forExpr, forScope);
            DiscoverBlockExpressions(forExpr.Collection, scope, containingSymbol, containingMethod);
            if (forExpr.Declaration is not null)
            {
                ResolveLocalVariableDeclaration(forExpr.Declaration, forScope, containingSymbol);
            }
            if (forExpr.VariableExpression is not null)
            {
                DiscoverBlockExpressions(forExpr.VariableExpression, scope, containingSymbol, containingMethod);
            }
            DiscoverBlockExpressions(forExpr.Body, forScope, containingSymbol, containingMethod);
        }
        else if (expr is TryExpression tryExpr)
        {
            DiscoverBlockExpressions(tryExpr.TryBlock, scope, containingSymbol, containingMethod);
            foreach (var catchClause in tryExpr.CatchClauses)
            {
                var catchScope = context.CreateScope(scope);
                context.RegisterSyntaxScope(catchClause, catchScope);
                if (catchClause.VariableIdentifier is { } varId && varId.Value != "_")
                {
                    var varPart = new SymbolPart(varId.Value);
                    context.CreateLocalVariableSymbol(catchScope, varPart, containingSymbol, null);
                }
                DiscoverBlockExpressions(catchClause.Block, catchScope, containingSymbol, containingMethod);
            }
            if (tryExpr.FinallyClause is { } finallyClause)
            {
                DiscoverBlockExpressions(finallyClause.Block, scope, containingSymbol, containingMethod);
            }
        }
        else if (expr is LambdaExpression lambda)
        {
            Scope lambdaScope = context.CreateScope(scope);
            context.RegisterSyntaxScope(lambda, lambdaScope);

            foreach (var p in lambda.Parameters)
            {
                var paramPart = ResolutionContext.GetSymbolName(p.Identifier).Last;
                context.CreateParameterSymbol(lambdaScope, paramPart, containingSymbol, null);
            }

            if (lambda.Body is BlockExpression lambdaBlock)
            {
                Scope blockScope = context.CreateScope(lambdaScope);
                context.RegisterSyntaxScope(lambdaBlock, blockScope);

                foreach (var child in lambdaBlock.Locals)
                    ResolveLocal(child, blockScope, containingSymbol, containingMethod);

                DiscoverBlockExpressions(lambdaBlock.FinalExpression, blockScope, containingSymbol, containingMethod);
            }
            else
            {
                DiscoverBlockExpressions(lambda.Body, lambdaScope, containingSymbol, containingMethod);
            }
        }
    }



    private void RegisterLocalFunction(SymbolHandle owner, LocalFunctionSymbol local)
    {
        if (owner.Kind is SymbolKind.Function)
            context.GetFunctionSymbol(owner)?.LocalFunctions.Add(ResolutionContext.GetHandle(local));
        else if (owner.Kind is SymbolKind.Method)
            context.GetMethodSymbol(owner)?.LocalFunctions.Add(ResolutionContext.GetHandle(local));
    }

    private void RegisterLocalAttribute(SymbolHandle owner, LocalAttributeSymbol local)
    {
        if (owner.Kind is SymbolKind.Function)
            context.GetFunctionSymbol(owner)?.LocalFunctions.Add(ResolutionContext.GetHandle(local));
        else if (owner.Kind is SymbolKind.Method)
            context.GetMethodSymbol(owner)?.LocalFunctions.Add(ResolutionContext.GetHandle(local));
    }

    private void RegisterLocalType(SymbolHandle owner, LocalTypeSymbol local)
    {
        if (owner.Kind is SymbolKind.Function)
            context.GetFunctionSymbol(owner)?.LocalTypes.Add(ResolutionContext.GetHandle(local));
        else if (owner.Kind is SymbolKind.Method)
            context.GetMethodSymbol(owner)?.LocalTypes.Add(ResolutionContext.GetHandle(local));
    }

    private List<SymbolHandle> ResolveGenericParameters(NamedSyntax name, Scope scope, Symbol genericSymbol)
    {
        var genericName = GetGenericName(name);

        if (genericName is null)
            return [];

        return ResolveGenericParametersFromList(genericName.GenericParameters, scope, genericSymbol);
    }

    private List<SymbolHandle> ResolveGenericParametersFromList(SeparatedSyntaxList<GenericParameterSyntax> list, Scope scope, Symbol genericSymbol)
    {
        var genericParameters = new List<SymbolHandle>(list.Count);

        foreach (var genericParameter in list)
        {
            var symbol = context.CreateGenericParameterSymbol(scope, new SymbolPart(genericParameter.Identifier), genericSymbol, genericParameter.Kind, genericParameter.IsVariadic, genericParameter);
            genericParameters.Add(ResolutionContext.GetHandle(symbol));
        }

        return genericParameters;
    }

    public static (Accessibility Direct, Accessibility Extension) ResolveFieldAccessibility(IReadOnlyList<Token> modifiers)
    {
        int extIndex = -1;
        for (int i = 0; i < modifiers.Count; i++)
        {
            if (modifiers[i].MatchingKind == MatchingKeywordKind.Ext)
            {
                extIndex = i;
                break;
            }
        }

        if (extIndex < 0)
        {
            foreach (var mod in modifiers)
            {
                if (mod.MatchingKind == MatchingKeywordKind.Public)
                    return (Accessibility.Public, Accessibility.Public);
                if (mod.MatchingKind == MatchingKeywordKind.Internal)
                    return (Accessibility.Internal, Accessibility.Internal);
                if (mod.MatchingKind == MatchingKeywordKind.Protected)
                    return (Accessibility.Protected, Accessibility.Protected);
                if (mod.MatchingKind == MatchingKeywordKind.Private)
                    return (Accessibility.Private, Accessibility.Private);
            }
            return (Accessibility.Private, Accessibility.Private);
        }

        Accessibility direct = Accessibility.Private;
        for (int i = 0; i < extIndex; i++)
        {
            var kind = modifiers[i].MatchingKind;
            if (kind == MatchingKeywordKind.Public) direct = Accessibility.Public;
            else if (kind == MatchingKeywordKind.Internal) direct = Accessibility.Internal;
            else if (kind == MatchingKeywordKind.Protected) direct = Accessibility.Protected;
            else if (kind == MatchingKeywordKind.Private) direct = Accessibility.Private;
        }

        Accessibility? ext = null;
        for (int i = extIndex + 1; i < modifiers.Count; i++)
        {
            var kind = modifiers[i].MatchingKind;
            if (kind == MatchingKeywordKind.Public) ext = Accessibility.Public;
            else if (kind == MatchingKeywordKind.Internal) ext = Accessibility.Internal;
            else if (kind == MatchingKeywordKind.Protected) ext = Accessibility.Protected;
            else if (kind == MatchingKeywordKind.Private) ext = Accessibility.Private;
        }

        if (ext is null)
        {
            ext = (direct == Accessibility.Internal) ? Accessibility.Public : Accessibility.Internal;
        }

        return (direct, ext.Value);
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
                MatchingKeywordKind.Private => TypeFlags.Private,
                MatchingKeywordKind.Protected => TypeFlags.Protected,
                MatchingKeywordKind.Unsafe => TypeFlags.Unsafe,
                MatchingKeywordKind.Unsealed => ~TypeFlags.Sealed,
                MatchingKeywordKind.Static => TypeFlags.Static,
                MatchingKeywordKind.Partial => TypeFlags.Partial,
                MatchingKeywordKind.Untagged => TypeFlags.Untagged,
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

    private static VariableFlags ResolveVariableFlags(IReadOnlyList<Token> modifiers, Accessibility? directAccessibility = null)
    {
        var flags = VariableFlags.None;

        foreach (var mod in modifiers)
        {
            flags |= mod.MatchingKind switch
            {
                MatchingKeywordKind.Static => VariableFlags.Static,
                MatchingKeywordKind.Const => VariableFlags.Const,
                _ => VariableFlags.None
            };
        }

        if (directAccessibility is not null)
        {
            flags |= directAccessibility.Value switch
            {
                Accessibility.Public => VariableFlags.Public,
                Accessibility.Internal => VariableFlags.Internal,
                Accessibility.Protected => VariableFlags.Protected,
                _ => VariableFlags.Private
            };
        }
        else
        {
            foreach (var mod in modifiers)
            {
                flags |= mod.MatchingKind switch
                {
                    MatchingKeywordKind.Public => VariableFlags.Public,
                    MatchingKeywordKind.Internal => VariableFlags.Internal,
                    MatchingKeywordKind.Private => VariableFlags.Private,
                    MatchingKeywordKind.Protected => VariableFlags.Protected,
                    _ => VariableFlags.None
                };
            }
        }

        return flags;
    }

    private static FunctionFlags ResolveFunctionFlags(IReadOnlyList<Token> modifiers)
    {
        var flags = FunctionFlags.None;

        foreach (var mod in modifiers)
        {
            flags |= mod.MatchingKind switch
            {
                MatchingKeywordKind.Public => FunctionFlags.Public,
                MatchingKeywordKind.Private => FunctionFlags.Private,
                MatchingKeywordKind.Protected => FunctionFlags.Protected,
                MatchingKeywordKind.Internal => FunctionFlags.Internal,
                MatchingKeywordKind.Static => FunctionFlags.Static,
                MatchingKeywordKind.Virtual => FunctionFlags.Virtual,
                MatchingKeywordKind.Unsafe => FunctionFlags.Unsafe,
                MatchingKeywordKind.Partial => FunctionFlags.Partial,
                _ => FunctionFlags.None
            };
        }

        return flags;
    }

    private static SymbolPart GetSpecialFunctionSymbolName(FunctionDeclaration declaration) =>
        ResolutionContext.GetSymbolName(declaration.Signature.Identifier).Last;
}
