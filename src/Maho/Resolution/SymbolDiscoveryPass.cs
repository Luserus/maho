using System;
using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Symbols;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

/// <summary>
/// First semantic pass. It predeclares symbols for each scope and records the scope/symbol tables
/// that later passes build on.
/// </summary>
internal sealed class SymbolDiscoveryPass : ResolutionPass
{
    private ResolutionContext context = null!;
    private DiagnosticsManager Diagnostics => context.Diagnostics;

    /// <summary>
    /// Runs the declaration pipeline in two phases so same-scope declarations exist before later
    /// passes interpret declaration signatures and bodies.
    /// </summary>
    public override void ExecuteUnit(ResolutionContext context)
    {
        this.context = context;

        PredeclareTopLevels(context.Root.Members, context.GlobalScope, context.GlobalNamespace);
        ResolveTopLevels(context.Root.Members, context.GlobalScope, context.GlobalNamespace);
    }

    private void PredeclareTopLevels(IReadOnlyList<TopLevel> members, Scope scope, Symbol containerSymbol)
    {
        // A namespace with a semicolon acts like a file-scoped namespace: every following
        // top-level declaration belongs to that namespace until this member list ends.
        Scope currentScope = scope;
        Symbol currentContainerSymbol = containerSymbol;

        for (int i = 0; i < members.Count; i++)
        {
            TopLevel member = members[i];
            PredeclareTopLevel(member, currentScope, currentContainerSymbol);

            if (member is NamespaceDeclaration { Body: NamespaceEmptyBody } namespaceDeclaration)
                (currentScope, currentContainerSymbol) = ResolveNamespaceContinuation(namespaceDeclaration, currentScope, currentContainerSymbol);
        }
    }

    private void ResolveTopLevels(IReadOnlyList<TopLevel> members, Scope scope, Symbol containerSymbol)
    {
        Scope currentScope = scope;
        Symbol currentContainerSymbol = containerSymbol;

        for (int i = 0; i < members.Count; i++)
        {
            TopLevel member = members[i];
            ResolveTopLevel(member, currentScope, currentContainerSymbol);

            if (member is NamespaceDeclaration { Body: NamespaceEmptyBody } namespaceDeclaration)
                (currentScope, currentContainerSymbol) = ResolveNamespaceContinuation(namespaceDeclaration, currentScope, currentContainerSymbol);
        }
    }

    private void PredeclareTopLevel(TopLevel topLevel, Scope scope, Symbol containerSymbol)
    {
        switch (topLevel)
        {
            case NamespaceDeclaration namespaceDeclaration:
                PredeclareNamespaceDeclaration(namespaceDeclaration, scope, containerSymbol);
                break;

            case TopLevelTypeDeclaration typeDeclaration:
            {
                TypeSymbol symbol = PredeclareTypeDeclaration(typeDeclaration.Type, scope, containerSymbol);
                context.ResolveDeclaredSymbol(typeDeclaration, symbol);
                break;
            }

            case TopLevelFunctionDeclaration functionDeclaration:
            {
                FunctionSymbol symbol = PredeclareFunctionDeclaration(functionDeclaration.Function, scope, containerSymbol);
                context.ResolveDeclaredSymbol(functionDeclaration, symbol);
                break;
            }

            case TopLevelVariableDeclaration variableDeclaration:
                PredeclareVariableDeclaration(variableDeclaration.Declaration, scope, containerSymbol);
                break;

            case TopLevelStatement statement:
                PredeclareTopLevelStatement(statement, scope, containerSymbol);
                break;

            default:
                throw new InvalidOperationException($"Unhandled top-level syntax '{topLevel.GetType().Name}'.");
        }
    }

    private void ResolveTopLevel(TopLevel topLevel, Scope scope, Symbol containerSymbol)
    {
        switch (topLevel)
        {
            case NamespaceDeclaration namespaceDeclaration:
                ResolveNamespaceDeclaration(namespaceDeclaration, scope, containerSymbol);
                break;

            case TopLevelTypeDeclaration typeDeclaration:
                ResolveTypeDeclaration(typeDeclaration.Type, scope);
                break;

            case TopLevelFunctionDeclaration functionDeclaration:
                ResolveFunctionDeclaration(functionDeclaration.Function, scope);
                break;

            case TopLevelVariableDeclaration variableDeclaration:
                ResolveVariableDeclaration(variableDeclaration.Declaration, scope);
                break;

            case TopLevelStatement statement:
                ResolveTopLevelStatement(statement, scope, containerSymbol);
                break;

            default:
                throw new InvalidOperationException($"Unhandled top-level syntax '{topLevel.GetType().Name}'.");
        }
    }

    private void PredeclareNamespaceDeclaration(NamespaceDeclaration declaration, Scope scope, Symbol parentSymbol)
    {
        Scope currentScope = scope;
        Symbol currentSymbol = parentSymbol;

        foreach (SimpleName part in EnumerateSimpleNames(declaration.Name))
        {
            NamespaceSymbol namespaceSymbol = GetOrDeclareNamespace(part, currentScope, currentSymbol);
            currentScope = context.ResolveSymbolScope(namespaceSymbol, part, currentScope);
            currentSymbol = namespaceSymbol;
        }

        context.ResolveDeclaredSymbol(declaration, currentSymbol);
        context.ResolveScope(declaration, currentScope);
        context.ResolveScope(declaration.Body, currentScope);

        if (declaration.Body is NamespaceBlockBody blockBody)
            PredeclareTopLevels(blockBody.Members, currentScope, currentSymbol);
    }

    private void ResolveNamespaceDeclaration(NamespaceDeclaration declaration, Scope scope, Symbol parentSymbol)
    {
        Scope currentScope = scope;
        Symbol currentSymbol = parentSymbol;

        foreach (SimpleName part in EnumerateSimpleNames(declaration.Name))
        {
            currentSymbol = GetOrDeclareNamespace(part, currentScope, currentSymbol);
            currentScope = context.ResolveSymbolScope(currentSymbol, part, currentScope);
        }

        if (declaration.Body is NamespaceBlockBody blockBody)
            ResolveTopLevels(blockBody.Members, currentScope, currentSymbol);
    }

    private (Scope Scope, Symbol ContainerSymbol) ResolveNamespaceContinuation(NamespaceDeclaration declaration, Scope scope, Symbol parentSymbol)
    {
        Scope currentScope = scope;
        Symbol currentSymbol = parentSymbol;

        foreach (SimpleName part in EnumerateSimpleNames(declaration.Name))
        {
            currentSymbol = GetOrDeclareNamespace(part, currentScope, currentSymbol);
            currentScope = context.ResolveSymbolScope(currentSymbol, part, currentScope);
        }

        return (currentScope, currentSymbol);
    }

    private NamespaceSymbol GetOrDeclareNamespace(SimpleName nameSyntax, Scope scope, Symbol parentSymbol)
    {
        string name = nameSyntax.Name.Value;
        IReadOnlyList<Symbol> localSymbols = scope.LookupLocal(name);

        for (int i = 0; i < localSymbols.Count; i++)
        {
            if (localSymbols[i] is not NamespaceSymbol namespaceSymbol)
                continue;

            context.ResolveDeclaredSymbol(nameSyntax, namespaceSymbol);
            return namespaceSymbol;
        }

        NamespaceSymbol created = new(name, parentSymbol, nameSyntax);
        context.DeclareSymbol(nameSyntax, created, scope);
        return created;
    }

    private TypeSymbol PredeclareTypeDeclaration(TypeDeclaration declaration, Scope scope, Symbol parentSymbol)
    {
        int arity = GetDeclaredArity(declaration.Name);
        string name = GetDeclaredName(declaration.Name);
        TypeSymbol symbol = new(name, parentSymbol, declaration, arity);

        context.DeclareSymbol(declaration, symbol, scope);

        Scope typeScope = context.ResolveSymbolScope(symbol, declaration, scope);
        context.ResolveScope(declaration.Body, typeScope);

        IReadOnlyList<TypeParameterSymbol> typeParameters = PredeclareTypeParameters(declaration.Name, symbol, typeScope);
        symbol.ResolveTypeParameters(typeParameters);

        if (declaration.Body is TypeBlockBody blockBody)
            PredeclareMembers(blockBody.Members, typeScope, symbol);

        return symbol;
    }

    private void ResolveTypeDeclaration(TypeDeclaration declaration, Scope scope)
    {
        if (!context.TryResolveDeclaredSymbol(declaration, out Symbol? declared) || declared is not TypeSymbol symbol)
        {
            Diagnostics.ReportResolutionStateError(GetNamedSyntaxSpan(declaration.Name), $"type declaration '{GetDeclaredName(declaration.Name)}'");
            return;
        }

        Scope typeScope = context.ResolveSymbolScope(symbol, declaration, scope);
        ReportDuplicateTypeDeclarationIfNeeded(symbol, declaration.Name, scope);

        if (declaration.Body is TypeBlockBody blockBody)
            ResolveMembers(blockBody.Members, typeScope, symbol);
    }

    private void PredeclareMembers(IReadOnlyList<Member> members, Scope scope, Symbol containerSymbol)
    {
        for (int i = 0; i < members.Count; i++)
            PredeclareMember(members[i], scope, containerSymbol);
    }

    private void ResolveMembers(IReadOnlyList<Member> members, Scope scope, Symbol containerSymbol)
    {
        for (int i = 0; i < members.Count; i++)
            ResolveMember(members[i], scope, containerSymbol);
    }

    private void PredeclareMember(Member member, Scope scope, Symbol containerSymbol)
    {
        switch (member)
        {
            case MemberTypeDeclaration typeDeclaration:
            {
                TypeSymbol symbol = PredeclareTypeDeclaration(typeDeclaration.Type, scope, containerSymbol);
                context.ResolveDeclaredSymbol(typeDeclaration, symbol);
                break;
            }

            case MemberFunctionDeclaration functionDeclaration:
            {
                FunctionSymbol symbol = PredeclareFunctionDeclaration(functionDeclaration.Function, scope, containerSymbol);
                context.ResolveDeclaredSymbol(functionDeclaration, symbol);
                break;
            }

            case MemberFieldDeclaration fieldDeclaration:
                PredeclareVariableDeclaration(fieldDeclaration.Declaration, scope, containerSymbol);
                break;

            default:
                throw new InvalidOperationException($"Unhandled member syntax '{member.GetType().Name}'.");
        }
    }

    private void ResolveMember(Member member, Scope scope, Symbol containerSymbol)
    {
        switch (member)
        {
            case MemberTypeDeclaration typeDeclaration:
                ResolveTypeDeclaration(typeDeclaration.Type, scope);
                break;

            case MemberFunctionDeclaration functionDeclaration:
                ResolveFunctionDeclaration(functionDeclaration.Function, scope);
                break;

            case MemberFieldDeclaration fieldDeclaration:
                ResolveVariableDeclaration(fieldDeclaration.Declaration, scope);
                break;

            default:
                throw new InvalidOperationException($"Unhandled member syntax '{member.GetType().Name}'.");
        }
    }

    private FunctionSymbol PredeclareFunctionDeclaration(FunctionDeclaration declaration, Scope scope, Symbol parentSymbol)
    {
        int arity = GetDeclaredArity(declaration.Signature.Identifier);
        string name = GetDeclaredName(declaration.Signature.Identifier);
        FunctionSymbol symbol = new(name, parentSymbol, declaration, arity);

        context.DeclareSymbol(declaration, symbol, scope);

        Scope functionScope = context.ResolveSymbolScope(symbol, declaration, scope);
        context.ResolveDeclaredSymbol(declaration.Signature, symbol);
        context.ResolveScope(declaration.Signature, functionScope);
        context.ResolveScope(declaration.Body, functionScope);

        IReadOnlyList<TypeParameterSymbol> typeParameters = PredeclareTypeParameters(declaration.Signature.Identifier, symbol, functionScope);
        symbol.ResolveTypeParameters(typeParameters);

        IReadOnlyList<ParameterSymbol> parameters = PredeclareParameters(declaration.Signature.Parameters, functionScope, symbol);
        symbol.ResolveParameters(parameters);

        switch (declaration.Body)
        {
            case FunctionBlockBody blockBody:
                PredeclareLocals(blockBody.Locals, functionScope, symbol);
                break;

            case FunctionLambdaBody lambdaBody:
                PredeclareEmbeddedLocalStatement(lambdaBody.Statement, functionScope, symbol);
                break;

            case FunctionEmptyBody:
                break;

            default:
                throw new InvalidOperationException($"Unhandled function body '{declaration.Body.GetType().Name}'.");
        }

        return symbol;
    }

    private void ResolveFunctionDeclaration(FunctionDeclaration declaration, Scope scope)
    {
        if (!context.TryResolveDeclaredSymbol(declaration, out Symbol? declared) || declared is not FunctionSymbol symbol)
        {
            Diagnostics.ReportResolutionStateError(GetNamedSyntaxSpan(declaration.Signature.Identifier), $"function declaration '{GetDeclaredName(declaration.Signature.Identifier)}'");
            return;
        }

        Scope functionScope = context.ResolveSymbolScope(symbol, declaration, scope);

        switch (declaration.Body)
        {
            case FunctionBlockBody blockBody:
                ResolveLocals(blockBody.Locals, functionScope, symbol);
                break;

            case FunctionLambdaBody lambdaBody:
                ResolveEmbeddedLocalStatement(lambdaBody.Statement, functionScope, symbol);
                break;

            case FunctionEmptyBody:
                break;

            default:
                throw new InvalidOperationException($"Unhandled function body '{declaration.Body.GetType().Name}'.");
        }
    }

    private List<TypeParameterSymbol> PredeclareTypeParameters(NamedSyntax nameSyntax, Symbol ownerSymbol, Scope ownerScope)
    {
        if (nameSyntax is not GenericName genericName)
            return [];

        List<TypeParameterSymbol> typeParameters = new(genericName.TypeParameters.Count);

        for (int i = 0; i < genericName.TypeParameters.Count; i++)
        {
            SimpleName typeParameterName = genericName.TypeParameters[i];
            TypeParameterSymbol typeParameter = new(typeParameterName.Name.Value, ownerSymbol, typeParameterName, i);
            context.DeclareSymbol(typeParameterName, typeParameter, ownerScope);
            typeParameters.Add(typeParameter);
        }

        context.ResolveDeclaredSymbol(genericName, ownerSymbol);
        return typeParameters;
    }

    private List<ParameterSymbol> PredeclareParameters(SeparatedSyntaxList<Parameter> parameters, Scope scope, Symbol functionSymbol)
    {
        List<ParameterSymbol> resolvedParameters = new(parameters.Count);

        for (int i = 0; i < parameters.Count; i++)
        {
            Parameter parameter = parameters[i];
            string name = GetDeclaredName(parameter.Declarator.Identifier);
            ParameterSymbol symbol = new(name, functionSymbol, parameter, i);

            context.DeclareSymbol(parameter, symbol, scope);
            context.ResolveDeclaredSymbol(parameter.Declarator, symbol);
            resolvedParameters.Add(symbol);
        }

        return resolvedParameters;
    }

    private void PredeclareVariableDeclaration(VariableDeclaration declaration, Scope scope, Symbol parentSymbol)
    {
        foreach (VariableDeclarator declarator in declaration.Declarators)
        {
            string name = GetDeclaredName(declarator.Identifier);
            VariableSymbol symbol = new(name, parentSymbol, declarator);

            context.DeclareSymbol(declarator, symbol, scope);
        }
    }

    private void ResolveVariableDeclaration(VariableDeclaration declaration, Scope scope)
    {
        foreach (VariableDeclarator declarator in declaration.Declarators)
        {
            if (!context.TryResolveDeclaredSymbol(declarator, out Symbol? declared) || declared is not VariableSymbol symbol)
            {
                Diagnostics.ReportResolutionStateError(GetNamedSyntaxSpan(declarator.Identifier), $"variable declaration '{GetDeclaredName(declarator.Identifier)}'");
                continue;
            }
        }
    }

    private void PredeclareLocals(IReadOnlyList<Local> locals, Scope scope, Symbol containerSymbol)
    {
        for (int i = 0; i < locals.Count; i++)
            PredeclareLocal(locals[i], scope, containerSymbol);
    }

    private void ResolveLocals(IReadOnlyList<Local> locals, Scope scope, Symbol containerSymbol)
    {
        for (int i = 0; i < locals.Count; i++)
            ResolveLocal(locals[i], scope, containerSymbol);
    }

    private void PredeclareLocal(Local local, Scope scope, Symbol containerSymbol)
    {
        switch (local)
        {
            case LocalTypeDeclaration typeDeclaration:
            {
                TypeSymbol symbol = PredeclareTypeDeclaration(typeDeclaration.Type, scope, containerSymbol);
                context.ResolveDeclaredSymbol(typeDeclaration, symbol);
                break;
            }

            case LocalFunctionDeclaration functionDeclaration:
            {
                FunctionSymbol symbol = PredeclareFunctionDeclaration(functionDeclaration.Function, scope, containerSymbol);
                context.ResolveDeclaredSymbol(functionDeclaration, symbol);
                break;
            }

            case LocalVariableDeclarationStatement variableDeclaration:
                PredeclareVariableDeclaration(variableDeclaration.Declaration, scope, containerSymbol);
                break;

            case LocalStatement statement:
                PredeclareLocalStatement(statement, scope, containerSymbol);
                break;

            default:
                throw new InvalidOperationException($"Unhandled local syntax '{local.GetType().Name}'.");
        }
    }

    private void ResolveLocal(Local local, Scope scope, Symbol containerSymbol)
    {
        switch (local)
        {
            case LocalTypeDeclaration typeDeclaration:
                ResolveTypeDeclaration(typeDeclaration.Type, scope);
                break;

            case LocalFunctionDeclaration functionDeclaration:
                ResolveFunctionDeclaration(functionDeclaration.Function, scope);
                break;

            case LocalVariableDeclarationStatement variableDeclaration:
                ResolveVariableDeclaration(variableDeclaration.Declaration, scope);
                break;

            case LocalStatement statement:
                ResolveLocalStatement(statement, scope, containerSymbol);
                break;

            default:
                throw new InvalidOperationException($"Unhandled local syntax '{local.GetType().Name}'.");
        }
    }

    private void PredeclareTopLevelStatement(TopLevelStatement statement, Scope scope, Symbol containerSymbol)
    {
        switch (statement)
        {
            case TopLevelBlockStatement blockStatement:
                PredeclareLocalBlock(blockStatement, blockStatement.Locals, scope, containerSymbol);
                break;

            case TopLevelIfStatement ifStatement:
                PredeclareTopLevelStatement(ifStatement.ThenStatement, scope, containerSymbol);

                if (ifStatement.ElseStatement is not null)
                    PredeclareTopLevelStatement(ifStatement.ElseStatement.Statement, scope, containerSymbol);
                break;

            case TopLevelWhileStatement whileStatement:
                PredeclareTopLevelStatement(whileStatement.Statement, scope, containerSymbol);
                break;

            case TopLevelElseStatement elseStatement:
                PredeclareTopLevelStatement(elseStatement.Statement, scope, containerSymbol);
                break;

            case TopLevelExpressionStatement:
            case TopLevelReturnStatement:
            case TopLevelEmptyStatement:
                break;

            default:
                throw new InvalidOperationException($"Unhandled top-level statement '{statement.GetType().Name}'.");
        }
    }

    private void ResolveTopLevelStatement(TopLevelStatement statement, Scope scope, Symbol containerSymbol)
    {
        switch (statement)
        {
            case TopLevelBlockStatement blockStatement:
                ResolveLocalBlock(blockStatement, blockStatement.Locals, scope, containerSymbol);
                break;

            case TopLevelIfStatement ifStatement:
                ResolveTopLevelStatement(ifStatement.ThenStatement, scope, containerSymbol);

                if (ifStatement.ElseStatement is not null)
                    ResolveTopLevelStatement(ifStatement.ElseStatement.Statement, scope, containerSymbol);
                break;

            case TopLevelWhileStatement whileStatement:
                ResolveTopLevelStatement(whileStatement.Statement, scope, containerSymbol);
                break;

            case TopLevelElseStatement elseStatement:
                ResolveTopLevelStatement(elseStatement.Statement, scope, containerSymbol);
                break;

            case TopLevelExpressionStatement:
            case TopLevelReturnStatement:
            case TopLevelEmptyStatement:
                break;

            default:
                throw new InvalidOperationException($"Unhandled top-level statement '{statement.GetType().Name}'.");
        }
    }

    private void PredeclareLocalStatement(LocalStatement statement, Scope scope, Symbol containerSymbol)
    {
        switch (statement)
        {
            case LocalBlockStatement blockStatement:
                PredeclareLocalBlock(blockStatement, blockStatement.Locals, scope, containerSymbol);
                break;

            case LocalIfStatement ifStatement:
                PredeclareEmbeddedLocalStatement(ifStatement.ThenStatement, scope, containerSymbol);

                if (ifStatement.ElseStatement is not null)
                    PredeclareEmbeddedLocalStatement(ifStatement.ElseStatement.Statement, scope, containerSymbol);
                break;

            case LocalWhileStatement whileStatement:
                PredeclareEmbeddedLocalStatement(whileStatement.Body, scope, containerSymbol);
                break;

            case LocalElseStatement elseStatement:
                PredeclareEmbeddedLocalStatement(elseStatement.Statement, scope, containerSymbol);
                break;

            case LocalVariableDeclarationStatement variableDeclaration:
                PredeclareVariableDeclaration(variableDeclaration.Declaration, scope, containerSymbol);
                break;

            case LocalExpressionStatement:
            case LocalReturnStatement:
            case LocalEmptyStatement:
                break;

            default:
                throw new InvalidOperationException($"Unhandled local statement '{statement.GetType().Name}'.");
        }
    }

    private void ResolveLocalStatement(LocalStatement statement, Scope scope, Symbol containerSymbol)
    {
        switch (statement)
        {
            case LocalBlockStatement blockStatement:
                ResolveLocalBlock(blockStatement, blockStatement.Locals, scope, containerSymbol);
                break;

            case LocalIfStatement ifStatement:
                ResolveEmbeddedLocalStatement(ifStatement.ThenStatement, scope, containerSymbol);

                if (ifStatement.ElseStatement is not null)
                    ResolveEmbeddedLocalStatement(ifStatement.ElseStatement.Statement, scope, containerSymbol);
                break;

            case LocalWhileStatement whileStatement:
                ResolveEmbeddedLocalStatement(whileStatement.Body, scope, containerSymbol);
                break;

            case LocalElseStatement elseStatement:
                ResolveEmbeddedLocalStatement(elseStatement.Statement, scope, containerSymbol);
                break;

            case LocalVariableDeclarationStatement variableDeclaration:
                ResolveVariableDeclaration(variableDeclaration.Declaration, scope);
                break;

            case LocalExpressionStatement:
            case LocalReturnStatement:
            case LocalEmptyStatement:
                break;

            default:
                throw new InvalidOperationException($"Unhandled local statement '{statement.GetType().Name}'.");
        }
    }

    private void PredeclareLocalBlock(SyntaxNode boundary, IReadOnlyList<Local> locals, Scope scope, Symbol containerSymbol)
    {
        Scope blockScope = context.CreateChildScope(boundary, scope);
        PredeclareLocals(locals, blockScope, containerSymbol);
    }

    private void ResolveLocalBlock(SyntaxNode boundary, IReadOnlyList<Local> locals, Scope scope, Symbol containerSymbol)
    {
        if (!context.TryResolveScope(boundary, out Scope? blockScope) || blockScope is null)
        {
            Diagnostics.ReportResolutionStateError(GetSyntaxSpan(boundary), $"block '{boundary.GetType().Name}'");
            return;
        }

        ResolveLocals(locals, blockScope, containerSymbol);
    }

    private void PredeclareEmbeddedLocalStatement(LocalStatement statement, Scope scope, Symbol containerSymbol)
    {
        if (statement is LocalBlockStatement)
        {
            PredeclareLocalStatement(statement, scope, containerSymbol);
            return;
        }

        Scope statementScope = context.CreateChildScope(statement, scope);
        PredeclareLocalStatement(statement, statementScope, containerSymbol);
    }

    private void ResolveEmbeddedLocalStatement(LocalStatement statement, Scope scope, Symbol containerSymbol)
    {
        if (statement is LocalBlockStatement)
        {
            ResolveLocalStatement(statement, scope, containerSymbol);
            return;
        }

        if (!context.TryResolveScope(statement, out Scope? statementScope) || statementScope is null)
        {
            Diagnostics.ReportResolutionStateError(GetSyntaxSpan(statement), $"embedded statement '{statement.GetType().Name}'");
            return;
        }

        ResolveLocalStatement(statement, statementScope, containerSymbol);
    }

    private void ReportDuplicateTypeDeclarationIfNeeded(TypeSymbol symbol, NamedSyntax nameSyntax, Scope scope)
    {
        IReadOnlyList<Symbol> localSymbols = scope.LookupLocal(symbol.Name);

        for (int i = 0; i < localSymbols.Count; i++)
        {
            Symbol localSymbol = localSymbols[i];

            if (ReferenceEquals(localSymbol, symbol))
                return;

            if (localSymbol is TypeSymbol other && other.DeclarationKey == symbol.DeclarationKey)
            {
                Diagnostics.ReportDuplicateTypeDeclaration(GetNamedSyntaxSpan(nameSyntax), symbol.Name, symbol.Arity);
                return;
            }
        }
    }

    private static string GetDeclaredName(NamedSyntax name) => name switch
    {
        SimpleName simpleName => simpleName.Name.Value,
        GenericName genericName => genericName.Name.Value,
        QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetDeclaredName(qualifiedName.Parts[qualifiedName.Parts.Count - 1]),
        _ => throw new InvalidOperationException($"Unhandled named syntax '{name.GetType().Name}'.")
    };

    private static int GetDeclaredArity(NamedSyntax name) => name switch
    {
        GenericName genericName => genericName.TypeParameters.Count,
        QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetDeclaredArity(qualifiedName.Parts[qualifiedName.Parts.Count - 1]),
        _ => 0
    };

    private static IEnumerable<SimpleName> EnumerateSimpleNames(NamedSyntax name)
    {
        switch (name)
        {
            case SimpleName simpleName:
                yield return simpleName;
                yield break;

            case QualifiedName qualifiedName:
                foreach (NamedSyntax part in qualifiedName.Parts)
                {
                    foreach (SimpleName simplePart in EnumerateSimpleNames(part))
                        yield return simplePart;
                }
                yield break;

            default:
                throw new InvalidOperationException($"Unhandled named syntax '{name.GetType().Name}'.");
        }
    }

    private static TextSpan GetNamedSyntaxSpan(NamedSyntax name) => name switch
    {
        SimpleName simpleName => simpleName.Name.Span,
        GenericName genericName => TextSpan.FromBounds(genericName.Name.Span.Start, genericName.GreaterThanToken.Span.End),
        QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => TextSpan.FromBounds(
            GetNamedSyntaxSpan(qualifiedName.Parts[0]).Start,
            GetNamedSyntaxSpan(qualifiedName.Parts[qualifiedName.Parts.Count - 1]).End),
        _ => default
    };

    private static TextSpan GetSyntaxSpan(SyntaxNode syntax) => syntax switch
    {
        TypeDeclaration typeDeclaration => TextSpan.FromBounds(typeDeclaration.Keyword.Span.Start, GetNamedSyntaxSpan(typeDeclaration.Name).End),
        NamespaceDeclaration namespaceDeclaration => TextSpan.FromBounds(namespaceDeclaration.Keyword.Span.Start, GetNamedSyntaxSpan(namespaceDeclaration.Name).End),
        FunctionDeclaration functionDeclaration => TextSpan.FromBounds(GetNamedSyntaxSpan(functionDeclaration.Signature.Identifier).Start, functionDeclaration.Signature.CloseParen.Span.End),
        LocalBlockStatement blockStatement => TextSpan.FromBounds(blockStatement.OpenBrace.Span.Start, blockStatement.CloseBrace.Span.End),
        TopLevelBlockStatement blockStatement => TextSpan.FromBounds(blockStatement.OpenBrace.Span.Start, blockStatement.CloseBrace.Span.End),
        LocalStatement localStatement => GetLocalStatementSpan(localStatement),
        TopLevelStatement topLevelStatement => GetTopLevelStatementSpan(topLevelStatement),
        _ => default
    };

    private static TextSpan GetLocalStatementSpan(LocalStatement statement) => statement switch
    {
        LocalIfStatement ifStatement => TextSpan.FromBounds(ifStatement.Keyword.Span.Start, ifStatement.CloseParen.Span.End),
        LocalWhileStatement whileStatement => TextSpan.FromBounds(whileStatement.Keyword.Span.Start, whileStatement.CloseParen.Span.End),
        LocalElseStatement elseStatement => elseStatement.Keyword.Span,
        LocalVariableDeclarationStatement variableDeclaration => GetNamedSyntaxSpan(variableDeclaration.Declaration.Declarators[0].Identifier),
        LocalExpressionStatement expressionStatement => expressionStatement.Semicolon.Span,
        LocalReturnStatement returnStatement => returnStatement.Statement.Keyword.Span,
        LocalEmptyStatement emptyStatement => emptyStatement.Semicolon.Span,
        LocalBlockStatement blockStatement => TextSpan.FromBounds(blockStatement.OpenBrace.Span.Start, blockStatement.CloseBrace.Span.End),
        _ => default
    };

    private static TextSpan GetTopLevelStatementSpan(TopLevelStatement statement) => statement switch
    {
        TopLevelIfStatement ifStatement => TextSpan.FromBounds(ifStatement.Keyword.Span.Start, ifStatement.CloseParen.Span.End),
        TopLevelWhileStatement whileStatement => TextSpan.FromBounds(whileStatement.Keyword.Span.Start, whileStatement.CloseParen.Span.End),
        TopLevelElseStatement elseStatement => elseStatement.Keyword.Span,
        TopLevelExpressionStatement expressionStatement => expressionStatement.Semicolon.Span,
        TopLevelReturnStatement returnStatement => returnStatement.Statement.Keyword.Span,
        TopLevelEmptyStatement emptyStatement => emptyStatement.Semicolon.Span,
        TopLevelBlockStatement blockStatement => TextSpan.FromBounds(blockStatement.OpenBrace.Span.Start, blockStatement.CloseBrace.Span.End),
        _ => default
    };
}
