using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>
/// First semantic pass. Each compilation unit builds a fully declared unit-local symbol/scope graph
/// in parallel, then merge attaches those graphs into the shared project namespace and scope state.
/// </summary>
internal sealed class SymbolDiscoveryPass : ResolutionPass
{
    /// <summary>
    /// Symbol discovery builds real per-unit declaration graphs in parallel, then attaches them to
    /// canonical project-wide namespace and scope state in a deterministic merge phase.
    /// </summary>
    public override void Execute(ResolutionCoordinatorContext context)
    {
        UnitGraph[] graphs = new UnitGraph[context.Units.Length];
        Parallel.For(0, context.Units.Length, unitIndex => graphs[unitIndex] = CollectUnitGraph(context.Units[unitIndex].Root));

        for (int unitIndex = 0; unitIndex < context.Units.Length; unitIndex++)
            MergeUnitGraph(context.Units[unitIndex], graphs[unitIndex]);
    }

    /// <summary>
    /// Builds one compilation unit's declaration graph using a unit-local root namespace/scope. The
    /// graph contains real symbols and scopes, but they are not attached to shared project state yet.
    /// </summary>
    private static UnitGraph CollectUnitGraph(CompilationUnit root)
    {
        NamespaceSymbol unitRootNamespace = new(SymbolName.Empty, parentSymbol: null, root);
        Scope unitRootScope = new(parent: null, boundary: root, ownerSymbol: unitRootNamespace);
        Dictionary<Symbol, Scope> ownedScopes = new(ReferenceEqualityComparer.Instance)
        {
            [unitRootNamespace] = unitRootScope
        };

        return new UnitGraph(root, CollectTopLevels(root.Members, unitRootScope, unitRootNamespace));

        TopLevelDeclarationGraph[] CollectTopLevels(IReadOnlyList<TopLevel> members, Scope scope, Symbol containerSymbol)
        {
            TopLevelDeclarationGraph[] graphs = new TopLevelDeclarationGraph[members.Count];
            Scope currentScope = scope;
            Symbol currentContainerSymbol = containerSymbol;

            for (int i = 0; i < members.Count; i++)
            {
                TopLevelDeclarationGraph graph = CollectTopLevel(members[i], currentScope, currentContainerSymbol);
                graphs[i] = graph;

                if (graph is NamespaceTopLevelDeclarationGraph { Namespace.IsFileScoped: true } namespaceGraph)
                    (currentScope, currentContainerSymbol) = ResolveNamespaceContinuation(namespaceGraph.Namespace, currentScope, currentContainerSymbol);
            }

            return graphs;
        }

        TopLevelDeclarationGraph CollectTopLevel(TopLevel topLevel, Scope scope, Symbol containerSymbol) => topLevel switch
        {
            NamespaceDeclaration namespaceDeclaration => new NamespaceTopLevelDeclarationGraph(namespaceDeclaration, CollectNamespace(namespaceDeclaration, scope, containerSymbol)),
            TopLevelTypeDeclaration typeDeclaration => new TypeTopLevelDeclarationGraph(typeDeclaration, CollectTypeDeclaration(typeDeclaration.Type, scope, containerSymbol)),
            TopLevelFunctionDeclaration functionDeclaration => new FunctionTopLevelDeclarationGraph(functionDeclaration, CollectFunctionDeclaration(functionDeclaration.Function, scope, containerSymbol)),
            TopLevelVariableDeclaration variableDeclaration => new VariableTopLevelDeclarationGraph(variableDeclaration, CollectVariableDeclaration(variableDeclaration.Declaration, scope, containerSymbol)),
            TopLevelStatement statement => new StatementTopLevelDeclarationGraph(statement, CollectTopLevelStatement(statement, scope, containerSymbol)),
            _ => throw new InvalidOperationException($"Unhandled top-level syntax '{topLevel.GetType().Name}'.")
        };

        NamespaceDeclarationGraph CollectNamespace(NamespaceDeclaration declaration, Scope scope, Symbol parentSymbol)
        {
            NamespacePartGraph[] parts = new NamespacePartGraph[CountSimpleNames(declaration.Name)];
            int partIndex = 0;
            Scope currentScope = scope;
            Symbol currentSymbol = parentSymbol;
            CollectNamespaceParts(declaration.Name, parts, ref partIndex, ref currentScope, ref currentSymbol);

            return declaration.Body switch
            {
                NamespaceBlockBody blockBody => new NamespaceDeclarationGraph(declaration, parts, CollectTopLevels(blockBody.Members, currentScope, currentSymbol), isFileScoped: false),
                NamespaceEmptyBody => new NamespaceDeclarationGraph(declaration, parts, [], isFileScoped: true),
                _ => throw new InvalidOperationException($"Unhandled namespace body '{declaration.Body.GetType().Name}'.")
            };
        }

        TypeDeclarationGraph CollectTypeDeclaration(TypeDeclaration declaration, Scope scope, Symbol parentSymbol)
        {
            TypeSymbol symbol = new(GetDeclaredName(declaration.Name), parentSymbol, declaration, GetDeclaredArity(declaration.Name));
            scope.Declare(symbol);

            Scope typeScope = new(scope, declaration, symbol);
            ownedScopes.Add(symbol, typeScope);

            TypeParameterSymbol[] typeParameters = DeclareTypeParameters(declaration.Name, symbol, typeScope);
            symbol.ResolveTypeParameters(typeParameters);

            MemberDeclarationGraph[] members = declaration.Body is TypeBlockBody blockBody ? CollectMembers(blockBody.Members, typeScope, symbol) : [];

            return new TypeDeclarationGraph(declaration, symbol, scope, typeScope, members);
        }

        MemberDeclarationGraph[] CollectMembers(IReadOnlyList<Member> members, Scope scope, Symbol containerSymbol)
        {
            MemberDeclarationGraph[] graphs = new MemberDeclarationGraph[members.Count];

            for (int i = 0; i < members.Count; i++)
                graphs[i] = CollectMember(members[i], scope, containerSymbol);

            return graphs;
        }

        MemberDeclarationGraph CollectMember(Member member, Scope scope, Symbol containerSymbol) => member switch
        {
            MemberTypeDeclaration typeDeclaration => new TypeMemberDeclarationGraph(typeDeclaration, CollectTypeDeclaration(typeDeclaration.Type, scope, containerSymbol)),
            MemberFunctionDeclaration functionDeclaration => new FunctionMemberDeclarationGraph(functionDeclaration, CollectFunctionDeclaration(functionDeclaration.Function, scope, containerSymbol)),
            MemberFieldDeclaration fieldDeclaration => new VariableMemberDeclarationGraph(fieldDeclaration, CollectVariableDeclaration(fieldDeclaration.Declaration, scope, containerSymbol)),
            MemberPropertyDeclaration propertyDeclaration => new PropertyMemberDeclarationGraph(propertyDeclaration, CollectPropertyDeclaration(propertyDeclaration, scope, containerSymbol)),
            _ => throw new InvalidOperationException($"Unhandled member syntax '{member.GetType().Name}'.")
        };

        FunctionDeclarationGraph CollectFunctionDeclaration(FunctionDeclaration declaration, Scope scope, Symbol parentSymbol)
        {
            FunctionSymbol symbol = new(GetDeclaredName(declaration.Signature.Identifier), parentSymbol, declaration, GetDeclaredArity(declaration.Signature.Identifier));
            scope.Declare(symbol);

            Scope functionScope = new(scope, declaration, symbol);
            ownedScopes.Add(symbol, functionScope);

            TypeParameterSymbol[] typeParameters = DeclareTypeParameters(declaration.Signature.Identifier, symbol, functionScope);
            symbol.ResolveTypeParameters(typeParameters);

            ParameterSymbol[] parameters = DeclareParameters(declaration.Signature.Parameters, functionScope, symbol);
            symbol.ResolveParameters(parameters);

            FunctionBodyGraph body = declaration.Body switch
            {
                FunctionBlockBody blockBody => new FunctionBlockBodyGraph(blockBody, CollectLocals(blockBody.Locals, functionScope, symbol)),
                FunctionLambdaBody lambdaBody => new FunctionLambdaBodyGraph(lambdaBody, CollectEmbeddedLocalStatement(lambdaBody.Statement, functionScope, symbol)),
                FunctionEmptyBody emptyBody => new FunctionEmptyBodyGraph(emptyBody),
                _ => throw new InvalidOperationException($"Unhandled function body '{declaration.Body.GetType().Name}'.")
            };

            return new FunctionDeclarationGraph(declaration, symbol, scope, functionScope, body);
        }

        PropertyDeclarationGraph CollectPropertyDeclaration(MemberPropertyDeclaration declaration, Scope scope, Symbol parentSymbol)
        {
            PropertySymbol symbol = new(GetDeclaredName(declaration.Identifier), parentSymbol, declaration);
            scope.Declare(symbol);
            return new PropertyDeclarationGraph(declaration, symbol, scope);
        }

        TypeParameterSymbol[] DeclareTypeParameters(NamedSyntax nameSyntax, Symbol ownerSymbol, Scope ownerScope)
        {
            if (nameSyntax is not GenericName genericName)
                return [];

            TypeParameterSymbol[] symbols = new TypeParameterSymbol[genericName.TypeParameters.Count];

            for (int i = 0; i < genericName.TypeParameters.Count; i++)
            {
                SimpleName syntax = genericName.TypeParameters[i];
                TypeParameterSymbol symbol = new(SymbolName.FromToken(syntax.Name), ownerSymbol, syntax, i);
                ownerScope.Declare(symbol);
                symbols[i] = symbol;
            }

            return symbols;
        }

        ParameterSymbol[] DeclareParameters(SeparatedSyntaxList<Parameter> parameters, Scope scope, Symbol functionSymbol)
        {
            ParameterSymbol[] resolvedParameters = new ParameterSymbol[parameters.Count];

            for (int i = 0; i < parameters.Count; i++)
            {
                Parameter parameter = parameters[i];
                ParameterSymbol symbol = new(GetDeclaredName(parameter.Declarator.Identifier), functionSymbol, parameter, i);
                scope.Declare(symbol);
                resolvedParameters[i] = symbol;
            }

            return resolvedParameters;
        }

        VariableDeclarationGraph CollectVariableDeclaration(VariableDeclaration declaration, Scope scope, Symbol parentSymbol)
        {
            VariableDeclaratorGraph[] declarators = new VariableDeclaratorGraph[declaration.Declarators.Count];

            for (int i = 0; i < declaration.Declarators.Count; i++)
            {
                VariableDeclarator declarator = declaration.Declarators[i];
                VariableSymbol symbol = new(GetDeclaredName(declarator.Identifier), parentSymbol, declarator);
                scope.Declare(symbol);
                declarators[i] = new VariableDeclaratorGraph(declarator, symbol);
            }

            return new VariableDeclarationGraph(declaration, scope, declarators);
        }

        LocalDeclarationGraph[] CollectLocals(IReadOnlyList<Local> locals, Scope scope, Symbol containerSymbol)
        {
            LocalDeclarationGraph[] graphs = new LocalDeclarationGraph[locals.Count];

            for (int i = 0; i < locals.Count; i++)
                graphs[i] = CollectLocal(locals[i], scope, containerSymbol);

            return graphs;
        }

        LocalDeclarationGraph CollectLocal(Local local, Scope scope, Symbol containerSymbol) => local switch
        {
            LocalTypeDeclaration typeDeclaration => new TypeLocalDeclarationGraph(typeDeclaration, CollectTypeDeclaration(typeDeclaration.Type, scope, containerSymbol)),
            LocalFunctionDeclaration functionDeclaration => new FunctionLocalDeclarationGraph(functionDeclaration, CollectFunctionDeclaration(functionDeclaration.Function, scope, containerSymbol)),
            LocalStatement statement => new StatementLocalDeclarationGraph(statement, CollectLocalStatement(statement, scope, containerSymbol)),
            _ => throw new InvalidOperationException($"Unhandled local syntax '{local.GetType().Name}'.")
        };

        TopLevelStatementGraph CollectTopLevelStatement(TopLevelStatement statement, Scope scope, Symbol containerSymbol) => statement switch
        {
            TopLevelBlockStatement blockStatement => CollectTopLevelBlockStatement(blockStatement, scope, containerSymbol),
            TopLevelIfStatement ifStatement => new TopLevelIfStatementGraph(
                ifStatement,
                CollectTopLevelStatement(ifStatement.ThenStatement, scope, containerSymbol),
                ifStatement.ElseStatement is null ? null : CollectTopLevelStatement(ifStatement.ElseStatement.Statement, scope, containerSymbol)),
            TopLevelWhileStatement whileStatement => new TopLevelWhileStatementGraph(whileStatement, CollectTopLevelStatement(whileStatement.Statement, scope, containerSymbol)),
            TopLevelElseStatement elseStatement => new TopLevelElseStatementGraph(elseStatement, CollectTopLevelStatement(elseStatement.Statement, scope, containerSymbol)),
            TopLevelExpressionStatement expressionStatement => new SimpleTopLevelStatementGraph(expressionStatement),
            TopLevelReturnStatement returnStatement => new SimpleTopLevelStatementGraph(returnStatement),
            TopLevelEmptyStatement emptyStatement => new SimpleTopLevelStatementGraph(emptyStatement),
            _ => throw new InvalidOperationException($"Unhandled top-level statement '{statement.GetType().Name}'.")
        };

        LocalStatementGraph CollectEmbeddedLocalStatement(LocalStatement statement, Scope scope, Symbol containerSymbol)
        {
            if (statement is LocalBlockStatement)
                return CollectLocalStatement(statement, scope, containerSymbol);

            Scope statementScope = new(scope, statement);
            return CollectLocalStatement(statement, statementScope, containerSymbol, statementScope);
        }

        LocalStatementGraph CollectLocalStatement(LocalStatement statement, Scope scope, Symbol containerSymbol, Scope? declaredScope = null) => statement switch
        {
            LocalBlockStatement blockStatement => CollectLocalBlockStatement(blockStatement, scope, containerSymbol),
            LocalIfStatement ifStatement => new LocalIfStatementGraph(
                ifStatement,
                declaredScope,
                CollectEmbeddedLocalStatement(ifStatement.ThenStatement, scope, containerSymbol),
                ifStatement.ElseStatement is null ? null : CollectEmbeddedLocalStatement(ifStatement.ElseStatement.Statement, scope, containerSymbol)),
            LocalWhileStatement whileStatement => new LocalWhileStatementGraph(
                whileStatement,
                declaredScope,
                CollectEmbeddedLocalStatement(whileStatement.Body, scope, containerSymbol)),
            LocalElseStatement elseStatement => new LocalElseStatementGraph(
                elseStatement,
                declaredScope,
                CollectEmbeddedLocalStatement(elseStatement.Statement, scope, containerSymbol)),
            LocalVariableDeclarationStatement variableDeclaration => new LocalVariableStatementGraph(
                variableDeclaration,
                declaredScope,
                CollectVariableDeclaration(variableDeclaration.Declaration, scope, containerSymbol)),
            LocalExpressionStatement expressionStatement => new SimpleLocalStatementGraph(expressionStatement, declaredScope),
            LocalReturnStatement returnStatement => new SimpleLocalStatementGraph(returnStatement, declaredScope),
            LocalEmptyStatement emptyStatement => new SimpleLocalStatementGraph(emptyStatement, declaredScope),
            _ => throw new InvalidOperationException($"Unhandled local statement '{statement.GetType().Name}'.")
        };

        NamespaceSymbol GetOrDeclareLocalNamespace(SimpleName syntax, Scope scope, Symbol parentSymbol)
        {
            SymbolName name = SymbolName.FromToken(syntax.Name);
            IReadOnlyList<Symbol> localSymbols = scope.LookupLocal(name);

            foreach (Symbol symbol in localSymbols)
            {
                if (symbol is NamespaceSymbol namespaceSymbol)
                    return namespaceSymbol;
            }

            NamespaceSymbol created = new(name, parentSymbol, syntax);
            scope.Declare(created);
            ownedScopes.Add(created, new Scope(scope, syntax, created));
            return created;
        }

        TopLevelBlockStatementGraph CollectTopLevelBlockStatement(TopLevelBlockStatement statement, Scope scope, Symbol containerSymbol)
        {
            Scope blockScope = new(scope, statement);
            return new TopLevelBlockStatementGraph(statement, blockScope, CollectLocals(statement.Locals, blockScope, containerSymbol));
        }

        LocalBlockStatementGraph CollectLocalBlockStatement(LocalBlockStatement statement, Scope scope, Symbol containerSymbol)
        {
            Scope blockScope = new(scope, statement);
            return new LocalBlockStatementGraph(statement, blockScope, CollectLocals(statement.Locals, blockScope, containerSymbol));
        }

        (Scope Scope, Symbol ContainerSymbol) ResolveNamespaceContinuation(NamespaceDeclarationGraph graph, Scope scope, Symbol parentSymbol)
        {
            Scope currentScope = scope;
            Symbol currentSymbol = parentSymbol;

            foreach (NamespacePartGraph part in graph.Parts)
            {
                currentSymbol = part.Symbol;
                currentScope = part.Scope;
            }

            return (currentScope, currentSymbol);
        }

        void CollectNamespaceParts(NamedSyntax name, NamespacePartGraph[] parts, ref int partIndex, ref Scope currentScope, ref Symbol currentSymbol)
        {
            switch (name)
            {
                case SimpleName simpleName:
                {
                    NamespaceSymbol namespaceSymbol = GetOrDeclareLocalNamespace(simpleName, currentScope, currentSymbol);
                    Scope namespaceScope = ownedScopes[namespaceSymbol];
                    parts[partIndex++] = new NamespacePartGraph(simpleName, SymbolName.FromToken(simpleName.Name), namespaceSymbol, currentScope, namespaceScope);
                    currentScope = namespaceScope;
                    currentSymbol = namespaceSymbol;
                    break;
                }

                case QualifiedName qualifiedName:
                    foreach (NamedSyntax part in qualifiedName.Parts)
                        CollectNamespaceParts(part, parts, ref partIndex, ref currentScope, ref currentSymbol);
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled named syntax '{name.GetType().Name}'.");
            }
        }
    }

    /// <summary>
    /// Attaches one unit-local declaration graph to canonical project-wide namespace and scope
    /// state, reusing existing namespaces when multiple units contribute to the same path.
    /// </summary>
    private static void MergeUnitGraph(ResolutionContext context, UnitGraph graph)
    {
        AttachTopLevels(graph.TopLevels, context.GlobalScope, context.GlobalNamespace);

        void AttachTopLevels(TopLevelDeclarationGraph[] members, Scope scope, Symbol containerSymbol)
        {
            Scope currentScope = scope;
            Symbol currentContainerSymbol = containerSymbol;

            foreach (TopLevelDeclarationGraph member in members)
            {
                AttachTopLevel(member, currentScope, currentContainerSymbol);

                if (member is NamespaceTopLevelDeclarationGraph { Namespace.IsFileScoped: true } namespaceGraph)
                    (currentScope, currentContainerSymbol) = ResolveNamespaceContinuation(namespaceGraph.Namespace, currentScope, currentContainerSymbol);
            }
        }

        void AttachTopLevel(TopLevelDeclarationGraph topLevel, Scope scope, Symbol containerSymbol)
        {
            switch (topLevel)
            {
                case NamespaceTopLevelDeclarationGraph namespaceGraph:
                    AttachNamespace(namespaceGraph.Namespace, scope, containerSymbol);
                    break;

                case TypeTopLevelDeclarationGraph typeGraph:
                    AttachTypeDeclaration(typeGraph.Declaration, scope, containerSymbol);
                    context.ResolveDeclaredSymbol(typeGraph.Wrapper, typeGraph.Declaration.Symbol);
                    break;

                case FunctionTopLevelDeclarationGraph functionGraph:
                    AttachFunctionDeclaration(functionGraph.Declaration, scope, containerSymbol);
                    context.ResolveDeclaredSymbol(functionGraph.Wrapper, functionGraph.Declaration.Symbol);
                    break;

                case VariableTopLevelDeclarationGraph variableGraph:
                    AttachVariableDeclaration(variableGraph.Declaration, scope, containerSymbol);
                    break;

                case StatementTopLevelDeclarationGraph statementGraph:
                    AttachTopLevelStatement(statementGraph.Statement, scope, containerSymbol);
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled top-level declaration graph '{topLevel.GetType().Name}'.");
            }
        }

        void AttachNamespace(NamespaceDeclarationGraph graph, Scope scope, Symbol parentSymbol)
        {
            Scope currentScope = scope;
            Symbol currentSymbol = parentSymbol;

            foreach (NamespacePartGraph part in graph.Parts)
            {
                NamespaceSymbol namespaceSymbol = GetOrAttachNamespace(part, currentScope, currentSymbol);
                currentScope = context.ResolveSymbolScope(namespaceSymbol, part.Syntax, currentScope);
                currentSymbol = namespaceSymbol;
            }

            context.ResolveDeclaredSymbol(graph.Declaration, currentSymbol);
            context.ResolveScope(graph.Declaration, currentScope);
            context.ResolveScope(graph.Declaration.Body, currentScope);

            if (!graph.IsFileScoped)
                AttachTopLevels(graph.Members, currentScope, currentSymbol);
        }

        (Scope Scope, Symbol ContainerSymbol) ResolveNamespaceContinuation(NamespaceDeclarationGraph graph, Scope scope, Symbol parentSymbol)
        {
            Scope currentScope = scope;
            Symbol currentSymbol = parentSymbol;

            foreach (NamespacePartGraph part in graph.Parts)
            {
                currentSymbol = GetOrAttachNamespace(part, currentScope, currentSymbol);
                currentScope = context.ResolveSymbolScope(currentSymbol, part.Syntax, currentScope);
            }

            return (currentScope, currentSymbol);
        }

        NamespaceSymbol GetOrAttachNamespace(NamespacePartGraph part, Scope scope, Symbol parentSymbol)
        {
            IReadOnlyList<Symbol> localSymbols = scope.LookupLocal(part.Name);

            foreach (Symbol symbol in localSymbols)
            {
                if (symbol is not NamespaceSymbol namespaceSymbol)
                    continue;

                context.ResolveDeclaredSymbol(part.Syntax, namespaceSymbol);
                return namespaceSymbol;
            }

            MoveDeclaredSymbol(part.Symbol, part.DeclaringScope, scope);
            part.Symbol.Reparent(parentSymbol);
            part.Scope.Reparent(scope, part.Symbol);
            context.Project.ResolveSymbolScope(part.Symbol, part.Scope);
            context.ResolveDeclaredSymbol(part.Syntax, part.Symbol);
            context.ResolveScope(part.Syntax, part.Scope);
            return part.Symbol;
        }

        void AttachTypeDeclaration(TypeDeclarationGraph graph, Scope scope, Symbol parentSymbol)
        {
            MoveDeclaredSymbol(graph.Symbol, graph.DeclaringScope, scope);
            graph.Symbol.Reparent(parentSymbol);
            graph.Scope.Reparent(scope, graph.Symbol);
            context.Project.ResolveSymbolScope(graph.Symbol, graph.Scope);

            context.ResolveDeclaredSymbol(graph.Declaration, graph.Symbol);
            context.ResolveScope(graph.Declaration.Body, graph.Scope);

            BindTypeParameters(graph.Symbol.TypeParameters);
            ResolveTypeDeclarationClauses(graph.Declaration, graph.Symbol, graph.Symbol.TypeParameters);
            AttachMembers(graph.Members, graph.Scope, graph.Symbol);
        }

        void AttachMembers(MemberDeclarationGraph[] members, Scope scope, Symbol containerSymbol)
        {
            foreach (var member in members)
                AttachMember(member, scope, containerSymbol);
        }

        void AttachMember(MemberDeclarationGraph member, Scope scope, Symbol containerSymbol)
        {
            switch (member)
            {
                case TypeMemberDeclarationGraph typeGraph:
                    AttachTypeDeclaration(typeGraph.Declaration, scope, containerSymbol);
                    context.ResolveDeclaredSymbol(typeGraph.Wrapper, typeGraph.Declaration.Symbol);
                    break;

                case FunctionMemberDeclarationGraph functionGraph:
                    AttachFunctionDeclaration(functionGraph.Declaration, scope, containerSymbol);
                    context.ResolveDeclaredSymbol(functionGraph.Wrapper, functionGraph.Declaration.Symbol);
                    break;

                case VariableMemberDeclarationGraph variableGraph:
                    AttachVariableDeclaration(variableGraph.Declaration, scope, containerSymbol);
                    break;

                case PropertyMemberDeclarationGraph propertyGraph:
                    AttachPropertyDeclaration(propertyGraph.Declaration, scope, containerSymbol);
                    context.ResolveDeclaredSymbol(propertyGraph.Wrapper, propertyGraph.Declaration.Symbol);
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled member declaration graph '{member.GetType().Name}'.");
            }
        }

        void AttachFunctionDeclaration(FunctionDeclarationGraph graph, Scope scope, Symbol parentSymbol)
        {
            MoveDeclaredSymbol(graph.Symbol, graph.DeclaringScope, scope);
            graph.Symbol.Reparent(parentSymbol);
            graph.Scope.Reparent(scope, graph.Symbol);
            context.Project.ResolveSymbolScope(graph.Symbol, graph.Scope);

            context.ResolveDeclaredSymbol(graph.Declaration, graph.Symbol);
            context.ResolveDeclaredSymbol(graph.Declaration.Signature, graph.Symbol);
            context.ResolveScope(graph.Declaration.Signature, graph.Scope);
            context.ResolveScope(graph.Declaration.Body, graph.Scope);

            BindTypeParameters(graph.Symbol.TypeParameters);
            ResolveTypeConstraintClauses(graph.Declaration.Signature.Constraints, graph.Symbol, graph.Symbol.TypeParameters);
            BindParameters(graph.Symbol.Parameters);

            switch (graph.Body)
            {
                case FunctionBlockBodyGraph blockBody:
                    AttachLocals(blockBody.Locals, graph.Scope, graph.Symbol);
                    break;

                case FunctionLambdaBodyGraph lambdaBody:
                    AttachEmbeddedLocalStatement(lambdaBody.Statement, graph.Scope, graph.Symbol);
                    break;

                case FunctionEmptyBodyGraph:
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled function body graph '{graph.Body.GetType().Name}'.");
            }
        }

        void AttachPropertyDeclaration(PropertyDeclarationGraph graph, Scope scope, Symbol parentSymbol)
        {
            MoveDeclaredSymbol(graph.Symbol, graph.DeclaringScope, scope);
            graph.Symbol.Reparent(parentSymbol);
            context.ResolveDeclaredSymbol(graph.Declaration, graph.Symbol);
        }

        void ResolveTypeDeclarationClauses(TypeDeclaration declaration, TypeSymbol symbol, ReadOnlySpan<TypeParameterSymbol> typeParameters)
        {
            if (declaration.Base is not null)
                context.ResolveDeclaredSymbol(declaration.Base, symbol);

            ResolveTypeConstraintClauses(declaration.Constraints, symbol, typeParameters);
        }

        void ResolveTypeConstraintClauses(IReadOnlyList<TypeConstraintClause> clauses, Symbol ownerSymbol, ReadOnlySpan<TypeParameterSymbol> typeParameters)
        {
            foreach (TypeConstraintClause clause in clauses)
            {
                context.ResolveDeclaredSymbol(clause, ownerSymbol);
                ResolveConstraintTypeParameter(clause.TypeParameter, typeParameters);
            }
        }

        void ResolveConstraintTypeParameter(SimpleName syntax, ReadOnlySpan<TypeParameterSymbol> typeParameters)
        {
            SymbolName name = SymbolName.FromToken(syntax.Name);

            foreach (var symbol in typeParameters)
            {
                if (symbol.Name != name)
                    continue;

                context.ResolveDeclaredSymbol(syntax, symbol);
                return;
                }
        }

        void BindTypeParameters(ReadOnlySpan<TypeParameterSymbol> typeParameters)
        {
            foreach (TypeParameterSymbol typeParameter in typeParameters)
                context.ResolveDeclaredSymbol(typeParameter.Declaration, typeParameter);
        }

        void BindParameters(ReadOnlySpan<ParameterSymbol> parameters)
        {
            foreach (var param in parameters)
            {
                Parameter parameter = (Parameter)param.Declaration;
                context.ResolveDeclaredSymbol(parameter, param);
                context.ResolveDeclaredSymbol(parameter.Declarator, param);
            }
        }

        void AttachVariableDeclaration(VariableDeclarationGraph graph, Scope scope, Symbol parentSymbol)
        {
            foreach (var declarator in graph.Declarators)
            {
                VariableSymbol symbol = declarator.Symbol;
                MoveDeclaredSymbol(symbol, graph.DeclaringScope, scope);
                symbol.Reparent(parentSymbol);
                context.ResolveDeclaredSymbol(symbol.Declaration, symbol);
            }
        }

        void AttachLocals(LocalDeclarationGraph[] locals, Scope scope, Symbol containerSymbol)
        {
            foreach (var local in locals)
                AttachLocal(local, scope, containerSymbol);
        }

        void AttachLocal(LocalDeclarationGraph local, Scope scope, Symbol containerSymbol)
        {
            switch (local)
            {
                case TypeLocalDeclarationGraph typeGraph:
                    AttachTypeDeclaration(typeGraph.Declaration, scope, containerSymbol);
                    context.ResolveDeclaredSymbol(typeGraph.Wrapper, typeGraph.Declaration.Symbol);
                    break;

                case FunctionLocalDeclarationGraph functionGraph:
                    AttachFunctionDeclaration(functionGraph.Declaration, scope, containerSymbol);
                    context.ResolveDeclaredSymbol(functionGraph.Wrapper, functionGraph.Declaration.Symbol);
                    break;

                case StatementLocalDeclarationGraph statementGraph:
                    AttachLocalStatement(statementGraph.Statement, scope, containerSymbol);
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled local declaration graph '{local.GetType().Name}'.");
            }
        }

        void AttachTopLevelStatement(TopLevelStatementGraph statement, Scope scope, Symbol containerSymbol)
        {
            switch (statement)
            {
                case TopLevelBlockStatementGraph blockStatement:
                    blockStatement.Scope.Reparent(scope);
                    context.ResolveScope(blockStatement.Syntax, blockStatement.Scope);
                    AttachLocals(blockStatement.Locals, blockStatement.Scope, containerSymbol);
                    break;

                case TopLevelIfStatementGraph ifStatement:
                    AttachTopLevelStatement(ifStatement.ThenStatement, scope, containerSymbol);

                    if (ifStatement.ElseStatement is not null)
                        AttachTopLevelStatement(ifStatement.ElseStatement, scope, containerSymbol);
                    break;

                case TopLevelWhileStatementGraph whileStatement:
                    AttachTopLevelStatement(whileStatement.Statement, scope, containerSymbol);
                    break;

                case TopLevelElseStatementGraph elseStatement:
                    AttachTopLevelStatement(elseStatement.Statement, scope, containerSymbol);
                    break;

                case SimpleTopLevelStatementGraph:
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled top-level statement graph '{statement.GetType().Name}'.");
            }
        }

        void AttachLocalStatement(LocalStatementGraph statement, Scope scope, Symbol containerSymbol)
        {
            Scope currentScope = scope;

            if (statement.Scope is not null)
            {
                statement.Scope.Reparent(scope);
                context.ResolveScope(statement.Syntax, statement.Scope);
                currentScope = statement.Scope;
            }

            switch (statement)
            {
                case LocalBlockStatementGraph blockStatement:
                    AttachLocals(blockStatement.Locals, blockStatement.Scope, containerSymbol);
                    break;

                case LocalIfStatementGraph ifStatement:
                    AttachEmbeddedLocalStatement(ifStatement.ThenStatement, currentScope, containerSymbol);

                    if (ifStatement.ElseStatement is not null)
                        AttachEmbeddedLocalStatement(ifStatement.ElseStatement, currentScope, containerSymbol);
                    break;

                case LocalWhileStatementGraph whileStatement:
                    AttachEmbeddedLocalStatement(whileStatement.Statement, currentScope, containerSymbol);
                    break;

                case LocalElseStatementGraph elseStatement:
                    AttachEmbeddedLocalStatement(elseStatement.Statement, currentScope, containerSymbol);
                    break;

                case LocalVariableStatementGraph variableStatement:
                    AttachVariableDeclaration(variableStatement.Declaration, currentScope, containerSymbol);
                    break;

                case SimpleLocalStatementGraph:
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled local statement graph '{statement.GetType().Name}'.");
            }
        }

        void AttachEmbeddedLocalStatement(LocalStatementGraph statement, Scope scope, Symbol containerSymbol) => AttachLocalStatement(statement, scope, containerSymbol);

        static void MoveDeclaredSymbol(Symbol symbol, Scope fromScope, Scope toScope)
        {
            if (ReferenceEquals(fromScope, toScope))
                return;

            fromScope.Remove(symbol);
            toScope.Declare(symbol);
        }
    }

    /// <summary> Collected declaration graph for one compilation unit prior to canonical attachment. </summary>
    private sealed class UnitGraph
    {
        /// <summary> Compilation unit that produced this collected declaration graph. </summary>
        public CompilationUnit Root { get; }
        /// <summary> Top-level declaration graphs captured from the unit in source order. </summary>
        public TopLevelDeclarationGraph[] TopLevels { get; }

        /// <summary> Creates the collected declaration graph for one compilation unit. </summary>
        public UnitGraph(CompilationUnit root, TopLevelDeclarationGraph[] topLevels)
        {
            Root = root;
            TopLevels = topLevels;
        }
    }

    /// <summary> Base type for one top-level declaration graph captured during collection. </summary>
    private abstract class TopLevelDeclarationGraph
    {
        /// <summary> Original top-level syntax represented by this graph node. </summary>
        public TopLevel Syntax { get; }

        /// <summary> Creates one top-level graph wrapper around the original syntax node. </summary>
        protected TopLevelDeclarationGraph(TopLevel syntax) => Syntax = syntax;
    }

    /// <summary> Top-level graph wrapper for a namespace declaration. </summary>
    private sealed class NamespaceTopLevelDeclarationGraph : TopLevelDeclarationGraph
    {
        /// <summary> Collected namespace declaration graph. </summary>
        public NamespaceDeclarationGraph Namespace { get; }

        /// <summary> Creates one top-level namespace graph wrapper. </summary>
        public NamespaceTopLevelDeclarationGraph(NamespaceDeclaration syntax, NamespaceDeclarationGraph @namespace)
            : base(syntax) => Namespace = @namespace;
    }

    /// <summary> Top-level graph wrapper for a type declaration. </summary>
    private sealed class TypeTopLevelDeclarationGraph : TopLevelDeclarationGraph
    {
        /// <summary> Original wrapper syntax that introduced the type at top level. </summary>
        public TopLevelTypeDeclaration Wrapper { get; }
        /// <summary> Collected type declaration graph. </summary>
        public TypeDeclarationGraph Declaration { get; }

        /// <summary> Creates one top-level type graph wrapper. </summary>
        public TypeTopLevelDeclarationGraph(TopLevelTypeDeclaration wrapper, TypeDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    /// <summary> Top-level graph wrapper for a function declaration. </summary>
    private sealed class FunctionTopLevelDeclarationGraph : TopLevelDeclarationGraph
    {
        /// <summary> Original wrapper syntax that introduced the function at top level. </summary>
        public TopLevelFunctionDeclaration Wrapper { get; }
        /// <summary> Collected function declaration graph. </summary>
        public FunctionDeclarationGraph Declaration { get; }

        /// <summary> Creates one top-level function graph wrapper. </summary>
        public FunctionTopLevelDeclarationGraph(TopLevelFunctionDeclaration wrapper, FunctionDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    /// <summary> Top-level graph wrapper for a variable declaration statement. </summary>
    private sealed class VariableTopLevelDeclarationGraph : TopLevelDeclarationGraph
    {
        /// <summary> Original wrapper syntax that introduced the variable declaration at top level. </summary>
        public TopLevelVariableDeclaration Wrapper { get; }
        /// <summary> Collected variable declaration graph. </summary>
        public VariableDeclarationGraph Declaration { get; }

        /// <summary> Creates one top-level variable graph wrapper. </summary>
        public VariableTopLevelDeclarationGraph(TopLevelVariableDeclaration wrapper, VariableDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    /// <summary> Top-level graph wrapper for a statement subtree. </summary>
    private sealed class StatementTopLevelDeclarationGraph : TopLevelDeclarationGraph
    {
        /// <summary> Collected top-level statement graph. </summary>
        public TopLevelStatementGraph Statement { get; }

        /// <summary> Creates one top-level statement graph wrapper. </summary>
        public StatementTopLevelDeclarationGraph(TopLevelStatement syntax, TopLevelStatementGraph statement)
            : base(syntax) => Statement = statement;
    }

    /// <summary> Collected graph for one namespace declaration and any path parts it introduced. </summary>
    private sealed class NamespaceDeclarationGraph
    {
        /// <summary> Original namespace declaration syntax. </summary>
        public NamespaceDeclaration Declaration { get; }
        /// <summary> Simple namespace path parts in source order. </summary>
        public NamespacePartGraph[] Parts { get; }
        /// <summary> Nested top-level declarations inside the namespace body. </summary>
        public TopLevelDeclarationGraph[] Members { get; }
        /// <summary> Indicates whether the namespace continues the surrounding scope for later members. </summary>
        public bool IsFileScoped { get; }

        /// <summary> Creates the collected graph for one namespace declaration. </summary>
        public NamespaceDeclarationGraph(NamespaceDeclaration declaration, NamespacePartGraph[] parts, TopLevelDeclarationGraph[] members, bool isFileScoped)
        {
            Declaration = declaration;
            Parts = parts;
            Members = members;
            IsFileScoped = isFileScoped;
        }
    }

    /// <summary> Collected graph for one simple namespace path part. </summary>
    private sealed class NamespacePartGraph
    {
        /// <summary> Original simple-name syntax for this namespace path part. </summary>
        public SimpleName Syntax { get; }
        /// <summary> Canonical simple name value for this namespace path part. </summary>
        public SymbolName Name { get; }
        /// <summary> Namespace symbol collected for this path part. </summary>
        public NamespaceSymbol Symbol { get; }
        /// <summary> Scope where the namespace symbol was first declared during collection. </summary>
        public Scope DeclaringScope { get; }
        /// <summary> Scope owned by the namespace symbol. </summary>
        public Scope Scope { get; }

        /// <summary> Creates the collected graph for one simple namespace path part. </summary>
        public NamespacePartGraph(SimpleName syntax, SymbolName name, NamespaceSymbol symbol, Scope declaringScope, Scope scope)
        {
            Syntax = syntax;
            Name = name;
            Symbol = symbol;
            DeclaringScope = declaringScope;
            Scope = scope;
        }
    }

    /// <summary> Collected graph for one type declaration. </summary>
    private sealed class TypeDeclarationGraph
    {
        /// <summary> Original type declaration syntax. </summary>
        public TypeDeclaration Declaration { get; }
        /// <summary> Type symbol collected for this declaration. </summary>
        public TypeSymbol Symbol { get; }
        /// <summary> Scope where the type symbol was first declared during collection. </summary>
        public Scope DeclaringScope { get; }
        /// <summary> Scope owned by the type symbol. </summary>
        public Scope Scope { get; }
        /// <summary> Collected member graphs declared directly inside the type body. </summary>
        public MemberDeclarationGraph[] Members { get; }

        /// <summary> Creates the collected graph for one type declaration. </summary>
        public TypeDeclarationGraph(TypeDeclaration declaration, TypeSymbol symbol, Scope declaringScope, Scope scope, MemberDeclarationGraph[] members)
        {
            Declaration = declaration;
            Symbol = symbol;
            DeclaringScope = declaringScope;
            Scope = scope;
            Members = members;
        }
    }

    /// <summary> Base type for one member declaration graph captured during collection. </summary>
    private abstract class MemberDeclarationGraph
    {
        /// <summary> Original member syntax represented by this graph node. </summary>
        public Member Syntax { get; }

        /// <summary> Creates one member graph wrapper around the original syntax node. </summary>
        protected MemberDeclarationGraph(Member syntax) => Syntax = syntax;
    }

    /// <summary> Member graph wrapper for a nested type declaration. </summary>
    private sealed class TypeMemberDeclarationGraph : MemberDeclarationGraph
    {
        /// <summary> Original wrapper syntax that introduced the nested type. </summary>
        public MemberTypeDeclaration Wrapper { get; }
        /// <summary> Collected nested type declaration graph. </summary>
        public TypeDeclarationGraph Declaration { get; }

        /// <summary> Creates one nested-type member graph wrapper. </summary>
        public TypeMemberDeclarationGraph(MemberTypeDeclaration wrapper, TypeDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    /// <summary> Member graph wrapper for a nested function declaration. </summary>
    private sealed class FunctionMemberDeclarationGraph : MemberDeclarationGraph
    {
        /// <summary> Original wrapper syntax that introduced the nested function. </summary>
        public MemberFunctionDeclaration Wrapper { get; }
        /// <summary> Collected nested function declaration graph. </summary>
        public FunctionDeclarationGraph Declaration { get; }

        /// <summary> Creates one nested-function member graph wrapper. </summary>
        public FunctionMemberDeclarationGraph(MemberFunctionDeclaration wrapper, FunctionDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    /// <summary> Member graph wrapper for a field declaration. </summary>
    private sealed class VariableMemberDeclarationGraph : MemberDeclarationGraph
    {
        /// <summary> Original wrapper syntax that introduced the field declaration. </summary>
        public MemberFieldDeclaration Wrapper { get; }
        /// <summary> Collected variable declaration graph. </summary>
        public VariableDeclarationGraph Declaration { get; }

        /// <summary> Creates one field member graph wrapper. </summary>
        public VariableMemberDeclarationGraph(MemberFieldDeclaration wrapper, VariableDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    /// <summary> Member graph wrapper for a property declaration. </summary>
    private sealed class PropertyMemberDeclarationGraph : MemberDeclarationGraph
    {
        /// <summary> Original wrapper syntax that introduced the property declaration. </summary>
        public MemberPropertyDeclaration Wrapper { get; }
        /// <summary> Collected property declaration graph. </summary>
        public PropertyDeclarationGraph Declaration { get; }

        /// <summary> Creates one property member graph wrapper. </summary>
        public PropertyMemberDeclarationGraph(MemberPropertyDeclaration wrapper, PropertyDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    /// <summary> Collected graph for one function declaration. </summary>
    private sealed class FunctionDeclarationGraph
    {
        /// <summary> Original function declaration syntax. </summary>
        public FunctionDeclaration Declaration { get; }
        /// <summary> Function symbol collected for this declaration. </summary>
        public FunctionSymbol Symbol { get; }
        /// <summary> Scope where the function symbol was first declared during collection. </summary>
        public Scope DeclaringScope { get; }
        /// <summary> Scope owned by the function symbol. </summary>
        public Scope Scope { get; }
        /// <summary> Collected graph for the function body. </summary>
        public FunctionBodyGraph Body { get; }

        /// <summary> Creates the collected graph for one function declaration. </summary>
        public FunctionDeclarationGraph(FunctionDeclaration declaration, FunctionSymbol symbol, Scope declaringScope, Scope scope, FunctionBodyGraph body)
        {
            Declaration = declaration;
            Symbol = symbol;
            DeclaringScope = declaringScope;
            Scope = scope;
            Body = body;
        }
    }

    /// <summary> Collected graph for one property declaration. </summary>
    private sealed class PropertyDeclarationGraph
    {
        /// <summary> Original property declaration syntax. </summary>
        public MemberPropertyDeclaration Declaration { get; }
        /// <summary> Property symbol collected for this declaration. </summary>
        public PropertySymbol Symbol { get; }
        /// <summary> Scope where the property symbol was first declared during collection. </summary>
        public Scope DeclaringScope { get; }

        /// <summary> Creates the collected graph for one property declaration. </summary>
        public PropertyDeclarationGraph(MemberPropertyDeclaration declaration, PropertySymbol symbol, Scope declaringScope)
        {
            Declaration = declaration;
            Symbol = symbol;
            DeclaringScope = declaringScope;
        }
    }

    /// <summary> Base type for one collected function-body graph. </summary>
    private abstract class FunctionBodyGraph
    {
        /// <summary> Original function-body syntax represented by this graph node. </summary>
        public FunctionBody Syntax { get; }

        /// <summary> Creates one function-body graph wrapper around the original syntax node. </summary>
        protected FunctionBodyGraph(FunctionBody syntax) => Syntax = syntax;
    }

    /// <summary> Collected function-body graph for a block-bodied function. </summary>
    private sealed class FunctionBlockBodyGraph : FunctionBodyGraph
    {
        /// <summary> Collected locals declared inside the block body. </summary>
        public LocalDeclarationGraph[] Locals { get; }

        /// <summary> Creates the collected graph for a block-bodied function. </summary>
        public FunctionBlockBodyGraph(FunctionBlockBody syntax, LocalDeclarationGraph[] locals)
            : base(syntax) => Locals = locals;
    }

    /// <summary> Collected function-body graph for a lambda-bodied function. </summary>
    private sealed class FunctionLambdaBodyGraph : FunctionBodyGraph
    {
        /// <summary> Collected statement graph for the lambda body. </summary>
        public LocalStatementGraph Statement { get; }

        /// <summary> Creates the collected graph for a lambda-bodied function. </summary>
        public FunctionLambdaBodyGraph(FunctionLambdaBody syntax, LocalStatementGraph statement)
            : base(syntax) => Statement = statement;
    }

    /// <summary> Collected function-body graph for an empty-bodied function declaration. </summary>
    private sealed class FunctionEmptyBodyGraph : FunctionBodyGraph
    {
        /// <summary> Creates the collected graph for an empty-bodied function. </summary>
        public FunctionEmptyBodyGraph(FunctionEmptyBody syntax) : base(syntax) { }
    }

    /// <summary> Collected graph for one variable declaration. </summary>
    private sealed class VariableDeclarationGraph
    {
        /// <summary> Original variable declaration syntax. </summary>
        public VariableDeclaration Declaration { get; }
        /// <summary> Scope where the declaration was first collected. </summary>
        public Scope DeclaringScope { get; }
        /// <summary> Collected variable declarators introduced by the declaration. </summary>
        public VariableDeclaratorGraph[] Declarators { get; }

        /// <summary> Creates the collected graph for one variable declaration. </summary>
        public VariableDeclarationGraph(VariableDeclaration declaration, Scope declaringScope, VariableDeclaratorGraph[] declarators)
        {
            Declaration = declaration;
            DeclaringScope = declaringScope;
            Declarators = declarators;
        }
    }

    /// <summary> Collected graph for one variable declarator and its symbol. </summary>
    private sealed class VariableDeclaratorGraph
    {
        /// <summary> Original declarator syntax. </summary>
        public VariableDeclarator Declarator { get; }
        /// <summary> Variable symbol collected for the declarator. </summary>
        public VariableSymbol Symbol { get; }

        /// <summary> Creates the collected graph for one variable declarator. </summary>
        public VariableDeclaratorGraph(VariableDeclarator declarator, VariableSymbol symbol)
        {
            Declarator = declarator;
            Symbol = symbol;
        }
    }

    /// <summary> Base type for one collected local declaration graph. </summary>
    private abstract class LocalDeclarationGraph
    {
        /// <summary> Original local syntax represented by this graph node. </summary>
        public Local Syntax { get; }

        /// <summary> Creates one local graph wrapper around the original syntax node. </summary>
        protected LocalDeclarationGraph(Local syntax) => Syntax = syntax;
    }

    /// <summary> Local graph wrapper for a nested type declaration. </summary>
    private sealed class TypeLocalDeclarationGraph : LocalDeclarationGraph
    {
        /// <summary> Original wrapper syntax that introduced the local type. </summary>
        public LocalTypeDeclaration Wrapper { get; }
        /// <summary> Collected local type declaration graph. </summary>
        public TypeDeclarationGraph Declaration { get; }

        /// <summary> Creates one local-type graph wrapper. </summary>
        public TypeLocalDeclarationGraph(LocalTypeDeclaration wrapper, TypeDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    /// <summary> Local graph wrapper for a nested function declaration. </summary>
    private sealed class FunctionLocalDeclarationGraph : LocalDeclarationGraph
    {
        /// <summary> Original wrapper syntax that introduced the local function. </summary>
        public LocalFunctionDeclaration Wrapper { get; }
        /// <summary> Collected local function declaration graph. </summary>
        public FunctionDeclarationGraph Declaration { get; }

        /// <summary> Creates one local-function graph wrapper. </summary>
        public FunctionLocalDeclarationGraph(LocalFunctionDeclaration wrapper, FunctionDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    /// <summary> Local graph wrapper for a statement subtree. </summary>
    private sealed class StatementLocalDeclarationGraph : LocalDeclarationGraph
    {
        /// <summary> Collected local statement graph. </summary>
        public LocalStatementGraph Statement { get; }

        /// <summary> Creates one local-statement graph wrapper. </summary>
        public StatementLocalDeclarationGraph(LocalStatement syntax, LocalStatementGraph statement)
            : base(syntax) => Statement = statement;
    }

    /// <summary> Base type for one collected top-level statement graph. </summary>
    private abstract class TopLevelStatementGraph
    {
        /// <summary> Original top-level statement syntax represented by this graph node. </summary>
        public TopLevelStatement Syntax { get; }

        /// <summary> Creates one top-level statement graph wrapper around the original syntax node. </summary>
        protected TopLevelStatementGraph(TopLevelStatement syntax) => Syntax = syntax;
    }

    /// <summary> Collected graph for a top-level block statement. </summary>
    private sealed class TopLevelBlockStatementGraph : TopLevelStatementGraph
    {
        /// <summary> Scope introduced by the top-level block. </summary>
        public Scope Scope { get; }
        /// <summary> Collected locals declared directly inside the block. </summary>
        public LocalDeclarationGraph[] Locals { get; }

        /// <summary> Creates the collected graph for a top-level block statement. </summary>
        public TopLevelBlockStatementGraph(TopLevelBlockStatement syntax, Scope scope, LocalDeclarationGraph[] locals)
            : base(syntax)
        {
            Scope = scope;
            Locals = locals;
        }
    }

    /// <summary> Collected graph for a top-level if statement. </summary>
    private sealed class TopLevelIfStatementGraph : TopLevelStatementGraph
    {
        /// <summary> Collected graph for the then branch. </summary>
        public TopLevelStatementGraph ThenStatement { get; }
        /// <summary> Collected graph for the optional else branch. </summary>
        public TopLevelStatementGraph? ElseStatement { get; }

        /// <summary> Creates the collected graph for a top-level if statement. </summary>
        public TopLevelIfStatementGraph(TopLevelIfStatement syntax, TopLevelStatementGraph thenStatement, TopLevelStatementGraph? elseStatement)
            : base(syntax)
        {
            ThenStatement = thenStatement;
            ElseStatement = elseStatement;
        }
    }

    /// <summary> Collected graph for a top-level while statement. </summary>
    private sealed class TopLevelWhileStatementGraph : TopLevelStatementGraph
    {
        /// <summary> Collected graph for the loop body statement. </summary>
        public TopLevelStatementGraph Statement { get; }

        /// <summary> Creates the collected graph for a top-level while statement. </summary>
        public TopLevelWhileStatementGraph(TopLevelWhileStatement syntax, TopLevelStatementGraph statement)
            : base(syntax) => Statement = statement;
    }

    /// <summary> Collected graph for a top-level else statement. </summary>
    private sealed class TopLevelElseStatementGraph : TopLevelStatementGraph
    {
        /// <summary> Collected graph for the nested else-body statement. </summary>
        public TopLevelStatementGraph Statement { get; }

        /// <summary> Creates the collected graph for a top-level else statement. </summary>
        public TopLevelElseStatementGraph(TopLevelElseStatement syntax, TopLevelStatementGraph statement)
            : base(syntax) => Statement = statement;
    }

    /// <summary> Collected graph for a top-level statement that introduces no nested declarations. </summary>
    private sealed class SimpleTopLevelStatementGraph : TopLevelStatementGraph
    {
        /// <summary> Creates the collected graph for a simple top-level statement. </summary>
        public SimpleTopLevelStatementGraph(TopLevelStatement syntax) : base(syntax) { }
    }

    /// <summary> Base type for one collected local statement graph. </summary>
    private abstract class LocalStatementGraph
    {
        /// <summary> Original local statement syntax represented by this graph node. </summary>
        public LocalStatement Syntax { get; }
        /// <summary> Optional scope introduced by the local statement itself. </summary>
        public Scope? Scope { get; }

        /// <summary> Creates one local-statement graph wrapper around the original syntax node. </summary>
        protected LocalStatementGraph(LocalStatement syntax, Scope? scope = null)
        {
            Syntax = syntax;
            Scope = scope;
        }
    }

    /// <summary> Collected graph for a local block statement. </summary>
    private sealed class LocalBlockStatementGraph : LocalStatementGraph
    {
        /// <summary> Scope introduced by the local block. </summary>
        public new Scope Scope { get; }
        /// <summary> Collected locals declared directly inside the block. </summary>
        public LocalDeclarationGraph[] Locals { get; }

        /// <summary> Creates the collected graph for a local block statement. </summary>
        public LocalBlockStatementGraph(LocalBlockStatement syntax, Scope scope, LocalDeclarationGraph[] locals)
            : base(syntax, scope)
        {
            Scope = scope;
            Locals = locals;
        }
    }

    /// <summary> Collected graph for a local if statement. </summary>
    private sealed class LocalIfStatementGraph : LocalStatementGraph
    {
        /// <summary> Collected graph for the then branch. </summary>
        public LocalStatementGraph ThenStatement { get; }
        /// <summary> Collected graph for the optional else branch. </summary>
        public LocalStatementGraph? ElseStatement { get; }

        /// <summary> Creates the collected graph for a local if statement. </summary>
        public LocalIfStatementGraph(LocalIfStatement syntax, Scope? scope, LocalStatementGraph thenStatement, LocalStatementGraph? elseStatement)
            : base(syntax, scope)
        {
            ThenStatement = thenStatement;
            ElseStatement = elseStatement;
        }
    }

    /// <summary> Collected graph for a local while statement. </summary>
    private sealed class LocalWhileStatementGraph : LocalStatementGraph
    {
        /// <summary> Collected graph for the loop body statement. </summary>
        public LocalStatementGraph Statement { get; }

        /// <summary> Creates the collected graph for a local while statement. </summary>
        public LocalWhileStatementGraph(LocalWhileStatement syntax, Scope? scope, LocalStatementGraph statement)
            : base(syntax, scope) => Statement = statement;
    }

    /// <summary> Collected graph for a local else statement. </summary>
    private sealed class LocalElseStatementGraph : LocalStatementGraph
    {
        /// <summary> Collected graph for the nested else-body statement. </summary>
        public LocalStatementGraph Statement { get; }

        /// <summary> Creates the collected graph for a local else statement. </summary>
        public LocalElseStatementGraph(LocalElseStatement syntax, Scope? scope, LocalStatementGraph statement)
            : base(syntax, scope) => Statement = statement;
    }

    /// <summary> Collected graph for a local variable declaration statement. </summary>
    private sealed class LocalVariableStatementGraph : LocalStatementGraph
    {
        /// <summary> Collected variable declaration graph for the statement. </summary>
        public VariableDeclarationGraph Declaration { get; }

        /// <summary> Creates the collected graph for a local variable declaration statement. </summary>
        public LocalVariableStatementGraph(LocalVariableDeclarationStatement syntax, Scope? scope, VariableDeclarationGraph declaration)
            : base(syntax, scope) => Declaration = declaration;
    }

    /// <summary> Collected graph for a local statement that introduces no nested declarations. </summary>
    private sealed class SimpleLocalStatementGraph : LocalStatementGraph
    {
        /// <summary> Creates the collected graph for a simple local statement. </summary>
        public SimpleLocalStatementGraph(LocalStatement syntax, Scope? scope = null) : base(syntax, scope) { }
    }

    /// <summary> Extracts the declared simple name from any supported name syntax. </summary>
    private static SymbolName GetDeclaredName(NamedSyntax name) => name switch
    {
        SimpleName simpleName => SymbolName.FromToken(simpleName.Name),
        GenericName genericName => SymbolName.FromToken(genericName.Name),
        QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetDeclaredName(qualifiedName.Parts[^1]),
        _ => throw new InvalidOperationException($"Unhandled named syntax '{name.GetType().Name}'.")
    };

    /// <summary> Extracts the declared generic arity from any supported name syntax. </summary>
    private static int GetDeclaredArity(NamedSyntax name) => name switch
    {
        GenericName genericName => genericName.TypeParameters.Count,
        QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetDeclaredArity(qualifiedName.Parts[^1]),
        _ => 0
    };

    /// <summary> Counts how many simple namespace path parts one name syntax contributes. </summary>
    private static int CountSimpleNames(NamedSyntax name) => name switch
    {
        SimpleName => 1,
        QualifiedName qualifiedName => CountQualifiedParts(qualifiedName.Parts),
        _ => throw new InvalidOperationException($"Unhandled named syntax '{name.GetType().Name}'.")
    };

    /// <summary> Counts the total number of simple parts across a qualified namespace name. </summary>
    private static int CountQualifiedParts(SeparatedSyntaxList<NamedSyntax> parts)
    {
        int count = 0;

        foreach (NamedSyntax name in parts)
            count += CountSimpleNames(name);

        return count;
    }
}
