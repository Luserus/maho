using System;
using System.Collections.Generic;
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
    public override ResolutionExecutionMode ExecutionMode => ResolutionExecutionMode.ParallelCollectThenMerge;

    /// <summary> Collects one unit-local declaration graph without mutating shared project state. </summary>
    public override ResolutionPassUnitResult CollectUnit(ResolutionContext context) => new Collector(context.Root).Collect();

    /// <summary> Attaches one unit's collected declaration graph into the real project symbol graph. </summary>
    public override void MergeUnit(ResolutionCoordinatorContext projectContext, ResolutionContext unitContext, ResolutionPassUnitResult? result)
    {
        if (result is not UnitGraph graph)
        {
            unitContext.Diagnostics.ReportResolutionStateError(default, $"unit declaration graph '{unitContext.Root.GetType().Name}'");
            return;
        }

        new Merger(unitContext).Merge(graph);
    }

    /// <summary>
    /// Builds one compilation unit's declaration graph using a unit-local root namespace/scope. The
    /// graph contains real symbols and scopes, but they are not attached to shared project state yet.
    /// </summary>
    private sealed class Collector
    {
        private readonly CompilationUnit root;
        private readonly NamespaceSymbol unitRootNamespace;
        private readonly Scope unitRootScope;
        private readonly Dictionary<Symbol, Scope> ownedScopes = new(ReferenceEqualityComparer.Instance);

        public Collector(CompilationUnit root)
        {
            this.root = root;
            unitRootNamespace = new NamespaceSymbol(SymbolName.Empty, parentSymbol: null, root);
            unitRootScope = new Scope(parent: null, boundary: root, ownerSymbol: unitRootNamespace);
            ownedScopes.Add(unitRootNamespace, unitRootScope);
        }

        public UnitGraph Collect() => new UnitGraph(root, CollectTopLevels(root.Members, unitRootScope, unitRootNamespace));

        private TopLevelDeclarationGraph[] CollectTopLevels(IReadOnlyList<TopLevel> members, Scope scope, Symbol containerSymbol)
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

        private TopLevelDeclarationGraph CollectTopLevel(TopLevel topLevel, Scope scope, Symbol containerSymbol) => topLevel switch
        {
            NamespaceDeclaration namespaceDeclaration => new NamespaceTopLevelDeclarationGraph(namespaceDeclaration, CollectNamespace(namespaceDeclaration, scope, containerSymbol)),
            TopLevelTypeDeclaration typeDeclaration => new TypeTopLevelDeclarationGraph(typeDeclaration, CollectTypeDeclaration(typeDeclaration.Type, scope, containerSymbol)),
            TopLevelFunctionDeclaration functionDeclaration => new FunctionTopLevelDeclarationGraph(functionDeclaration, CollectFunctionDeclaration(functionDeclaration.Function, scope, containerSymbol)),
            TopLevelVariableDeclaration variableDeclaration => new VariableTopLevelDeclarationGraph(variableDeclaration, CollectVariableDeclaration(variableDeclaration.Declaration, scope, containerSymbol)),
            TopLevelStatement statement => new StatementTopLevelDeclarationGraph(statement, CollectTopLevelStatement(statement, scope, containerSymbol)),
            _ => throw new InvalidOperationException($"Unhandled top-level syntax '{topLevel.GetType().Name}'.")
        };

        private NamespaceDeclarationGraph CollectNamespace(NamespaceDeclaration declaration, Scope scope, Symbol parentSymbol)
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

        private TypeDeclarationGraph CollectTypeDeclaration(TypeDeclaration declaration, Scope scope, Symbol parentSymbol)
        {
            TypeSymbol symbol = new TypeSymbol(GetDeclaredName(declaration.Name), parentSymbol, declaration, GetDeclaredArity(declaration.Name));
            scope.Declare(symbol);

            Scope typeScope = new Scope(scope, declaration, symbol);
            ownedScopes.Add(symbol, typeScope);

            TypeParameterSymbol[] typeParameters = DeclareTypeParameters(declaration.Name, symbol, typeScope);
            symbol.ResolveTypeParameters(typeParameters);

            MemberDeclarationGraph[] members = declaration.Body is TypeBlockBody blockBody ? CollectMembers(blockBody.Members, typeScope, symbol) : [];

            return new TypeDeclarationGraph(declaration, symbol, scope, typeScope, members);
        }

        private MemberDeclarationGraph[] CollectMembers(IReadOnlyList<Member> members, Scope scope, Symbol containerSymbol)
        {
            MemberDeclarationGraph[] graphs = new MemberDeclarationGraph[members.Count];

            for (int i = 0; i < members.Count; i++)
                graphs[i] = CollectMember(members[i], scope, containerSymbol);

            return graphs;
        }

        private MemberDeclarationGraph CollectMember(Member member, Scope scope, Symbol containerSymbol) => member switch
        {
            MemberTypeDeclaration typeDeclaration => new TypeMemberDeclarationGraph(typeDeclaration, CollectTypeDeclaration(typeDeclaration.Type, scope, containerSymbol)),
            MemberFunctionDeclaration functionDeclaration => new FunctionMemberDeclarationGraph(functionDeclaration, CollectFunctionDeclaration(functionDeclaration.Function, scope, containerSymbol)),
            MemberFieldDeclaration fieldDeclaration => new VariableMemberDeclarationGraph(fieldDeclaration, CollectVariableDeclaration(fieldDeclaration.Declaration, scope, containerSymbol)),
            _ => throw new InvalidOperationException($"Unhandled member syntax '{member.GetType().Name}'.")
        };

        private FunctionDeclarationGraph CollectFunctionDeclaration(FunctionDeclaration declaration, Scope scope, Symbol parentSymbol)
        {
            FunctionSymbol symbol = new FunctionSymbol(GetDeclaredName(declaration.Signature.Identifier), parentSymbol, declaration, GetDeclaredArity(declaration.Signature.Identifier));
            scope.Declare(symbol);

            Scope functionScope = new Scope(scope, declaration, symbol);
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

        private static TypeParameterSymbol[] DeclareTypeParameters(NamedSyntax nameSyntax, Symbol ownerSymbol, Scope ownerScope)
        {
            if (nameSyntax is not GenericName genericName)
                return [];

            TypeParameterSymbol[] symbols = new TypeParameterSymbol[genericName.TypeParameters.Count];

            for (int i = 0; i < genericName.TypeParameters.Count; i++)
            {
                var typeParameterName = genericName.TypeParameters[i];
                var symbol = new TypeParameterSymbol(SymbolName.FromToken(typeParameterName.Name), ownerSymbol, typeParameterName, i);
                ownerScope.Declare(symbol);
                symbols[i] = symbol;
            }

            return symbols;
        }

        private static ParameterSymbol[] DeclareParameters(SeparatedSyntaxList<Parameter> parameters, Scope scope, Symbol functionSymbol)
        {
            ParameterSymbol[] resolvedParameters = new ParameterSymbol[parameters.Count];

            for (int i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                var symbol = new ParameterSymbol(GetDeclaredName(parameter.Declarator.Identifier), functionSymbol, parameter, i);
                scope.Declare(symbol);
                resolvedParameters[i] = symbol;
            }

            return resolvedParameters;
        }

        private static VariableDeclarationGraph CollectVariableDeclaration(VariableDeclaration declaration, Scope scope, Symbol parentSymbol)
        {
            VariableDeclaratorGraph[] declarators = new VariableDeclaratorGraph[declaration.Declarators.Count];

            for (int i = 0; i < declaration.Declarators.Count; i++)
            {
                var declarator = declaration.Declarators[i];
                var symbol = new VariableSymbol(GetDeclaredName(declarator.Identifier), parentSymbol, declarator);
                scope.Declare(symbol);
                declarators[i] = new VariableDeclaratorGraph(declarator, symbol);
            }

            return new VariableDeclarationGraph(declaration, scope, declarators);
        }

        private LocalDeclarationGraph[] CollectLocals(IReadOnlyList<Local> locals, Scope scope, Symbol containerSymbol)
        {
            LocalDeclarationGraph[] graphs = new LocalDeclarationGraph[locals.Count];

            for (int i = 0; i < locals.Count; i++)
                graphs[i] = CollectLocal(locals[i], scope, containerSymbol);

            return graphs;
        }

        private LocalDeclarationGraph CollectLocal(Local local, Scope scope, Symbol containerSymbol) => local switch
        {
            LocalTypeDeclaration typeDeclaration => new TypeLocalDeclarationGraph(typeDeclaration, CollectTypeDeclaration(typeDeclaration.Type, scope, containerSymbol)),
            LocalFunctionDeclaration functionDeclaration => new FunctionLocalDeclarationGraph(functionDeclaration, CollectFunctionDeclaration(functionDeclaration.Function, scope, containerSymbol)),
            LocalStatement statement => new StatementLocalDeclarationGraph(statement, CollectLocalStatement(statement, scope, containerSymbol)),
            _ => throw new InvalidOperationException($"Unhandled local syntax '{local.GetType().Name}'.")
        };

        private TopLevelStatementGraph CollectTopLevelStatement(TopLevelStatement statement, Scope scope, Symbol containerSymbol) => statement switch
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

        private LocalStatementGraph CollectEmbeddedLocalStatement(LocalStatement statement, Scope scope, Symbol containerSymbol)
        {
            if (statement is LocalBlockStatement)
                return CollectLocalStatement(statement, scope, containerSymbol);

            Scope statementScope = new(scope, statement);
            return CollectLocalStatement(statement, statementScope, containerSymbol, statementScope);
        }

        private LocalStatementGraph CollectLocalStatement(LocalStatement statement, Scope scope, Symbol containerSymbol, Scope? declaredScope = null) => statement switch
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

        private NamespaceSymbol GetOrDeclareLocalNamespace(SimpleName syntax, Scope scope, Symbol parentSymbol)
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

        private TopLevelBlockStatementGraph CollectTopLevelBlockStatement(TopLevelBlockStatement statement, Scope scope, Symbol containerSymbol)
        {
            Scope blockScope = new(scope, statement);
            return new TopLevelBlockStatementGraph(statement, blockScope, CollectLocals(statement.Locals, blockScope, containerSymbol));
        }

        private LocalBlockStatementGraph CollectLocalBlockStatement(LocalBlockStatement statement, Scope scope, Symbol containerSymbol)
        {
            Scope blockScope = new(scope, statement);
            return new LocalBlockStatementGraph(statement, blockScope, CollectLocals(statement.Locals, blockScope, containerSymbol));
        }

        private static (Scope Scope, Symbol ContainerSymbol) ResolveNamespaceContinuation(NamespaceDeclarationGraph graph, Scope scope, Symbol parentSymbol)
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

        private void CollectNamespaceParts(NamedSyntax name, NamespacePartGraph[] parts, ref int partIndex, ref Scope currentScope, ref Symbol currentSymbol)
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
                    foreach (var nm in qualifiedName.Parts)
                        CollectNamespaceParts(nm, parts, ref partIndex, ref currentScope, ref currentSymbol);
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
    private sealed class Merger
    {
        private readonly ResolutionContext context;

        public Merger(ResolutionContext context) => this.context = context;

        public void Merge(UnitGraph graph) => AttachTopLevels(graph.TopLevels, context.GlobalScope, context.GlobalNamespace);

        private void AttachTopLevels(TopLevelDeclarationGraph[] members, Scope scope, Symbol containerSymbol)
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

        private void AttachTopLevel(TopLevelDeclarationGraph topLevel, Scope scope, Symbol containerSymbol)
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

        private void AttachNamespace(NamespaceDeclarationGraph graph, Scope scope, Symbol parentSymbol)
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

        private (Scope Scope, Symbol ContainerSymbol) ResolveNamespaceContinuation(NamespaceDeclarationGraph graph, Scope scope, Symbol parentSymbol)
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

        private NamespaceSymbol GetOrAttachNamespace(NamespacePartGraph part, Scope scope, Symbol parentSymbol)
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

        private void AttachTypeDeclaration(TypeDeclarationGraph graph, Scope scope, Symbol parentSymbol)
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

        private void AttachMembers(MemberDeclarationGraph[] members, Scope scope, Symbol containerSymbol)
        {
            foreach (var member in members)
                AttachMember(member, scope, containerSymbol);
        }

        private void AttachMember(MemberDeclarationGraph member, Scope scope, Symbol containerSymbol)
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

                default:
                    throw new InvalidOperationException($"Unhandled member declaration graph '{member.GetType().Name}'.");
            }
        }

        private void AttachFunctionDeclaration(FunctionDeclarationGraph graph, Scope scope, Symbol parentSymbol)
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

        private void ResolveTypeDeclarationClauses(TypeDeclaration declaration, TypeSymbol symbol, ReadOnlySpan<TypeParameterSymbol> typeParameters)
        {
            if (declaration.Base is not null)
                context.ResolveDeclaredSymbol(declaration.Base, symbol);

            ResolveTypeConstraintClauses(declaration.Constraints, symbol, typeParameters);
        }

        private void ResolveTypeConstraintClauses(IReadOnlyList<TypeConstraintClause> clauses, Symbol ownerSymbol, ReadOnlySpan<TypeParameterSymbol> typeParameters)
        {
            foreach (TypeConstraintClause clause in clauses)
            {
                context.ResolveDeclaredSymbol(clause, ownerSymbol);
                ResolveConstraintTypeParameter(clause.TypeParameter, typeParameters);
            }
        }

        private void ResolveConstraintTypeParameter(SimpleName syntax, ReadOnlySpan<TypeParameterSymbol> typeParameters)
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

        private void BindTypeParameters(ReadOnlySpan<TypeParameterSymbol> typeParameters)
        {
            foreach (TypeParameterSymbol typeParameter in typeParameters)
                context.ResolveDeclaredSymbol(typeParameter.Declaration, typeParameter);
        }

        private void BindParameters(ReadOnlySpan<ParameterSymbol> parameters)
        {
            foreach (var param in parameters)
            {
                Parameter parameter = (Parameter)param.Declaration;
                context.ResolveDeclaredSymbol(parameter, param);
                context.ResolveDeclaredSymbol(parameter.Declarator, param);
            }
        }

        private void AttachVariableDeclaration(VariableDeclarationGraph graph, Scope scope, Symbol parentSymbol)
        {
            foreach (var declarator in graph.Declarators)
            {
                VariableSymbol symbol = declarator.Symbol;
                MoveDeclaredSymbol(symbol, graph.DeclaringScope, scope);
                symbol.Reparent(parentSymbol);
                context.ResolveDeclaredSymbol(symbol.Declaration, symbol);
            }
        }

        private void AttachLocals(LocalDeclarationGraph[] locals, Scope scope, Symbol containerSymbol)
        {
            foreach (var local in locals)
                AttachLocal(local, scope, containerSymbol);
        }

        private void AttachLocal(LocalDeclarationGraph local, Scope scope, Symbol containerSymbol)
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

        private void AttachTopLevelStatement(TopLevelStatementGraph statement, Scope scope, Symbol containerSymbol)
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

        private void AttachLocalStatement(LocalStatementGraph statement, Scope scope, Symbol containerSymbol)
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

        private void AttachEmbeddedLocalStatement(LocalStatementGraph statement, Scope scope, Symbol containerSymbol) => AttachLocalStatement(statement, scope, containerSymbol);

        private static void MoveDeclaredSymbol(Symbol symbol, Scope fromScope, Scope toScope)
        {
            if (ReferenceEquals(fromScope, toScope))
                return;

            fromScope.Remove(symbol);
            toScope.Declare(symbol);
        }
    }

    private sealed class UnitGraph : ResolutionPassUnitResult
    {
        public CompilationUnit Root { get; }
        public TopLevelDeclarationGraph[] TopLevels { get; }

        public UnitGraph(CompilationUnit root, TopLevelDeclarationGraph[] topLevels)
        {
            Root = root;
            TopLevels = topLevels;
        }
    }

    private abstract class TopLevelDeclarationGraph
    {
        public TopLevel Syntax { get; }

        protected TopLevelDeclarationGraph(TopLevel syntax) => Syntax = syntax;
    }

    private sealed class NamespaceTopLevelDeclarationGraph : TopLevelDeclarationGraph
    {
        public NamespaceDeclarationGraph Namespace { get; }

        public NamespaceTopLevelDeclarationGraph(NamespaceDeclaration syntax, NamespaceDeclarationGraph @namespace)
            : base(syntax) => Namespace = @namespace;
    }

    private sealed class TypeTopLevelDeclarationGraph : TopLevelDeclarationGraph
    {
        public TopLevelTypeDeclaration Wrapper { get; }
        public TypeDeclarationGraph Declaration { get; }

        public TypeTopLevelDeclarationGraph(TopLevelTypeDeclaration wrapper, TypeDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    private sealed class FunctionTopLevelDeclarationGraph : TopLevelDeclarationGraph
    {
        public TopLevelFunctionDeclaration Wrapper { get; }
        public FunctionDeclarationGraph Declaration { get; }

        public FunctionTopLevelDeclarationGraph(TopLevelFunctionDeclaration wrapper, FunctionDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    private sealed class VariableTopLevelDeclarationGraph : TopLevelDeclarationGraph
    {
        public TopLevelVariableDeclaration Wrapper { get; }
        public VariableDeclarationGraph Declaration { get; }

        public VariableTopLevelDeclarationGraph(TopLevelVariableDeclaration wrapper, VariableDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    private sealed class StatementTopLevelDeclarationGraph : TopLevelDeclarationGraph
    {
        public TopLevelStatementGraph Statement { get; }

        public StatementTopLevelDeclarationGraph(TopLevelStatement syntax, TopLevelStatementGraph statement)
            : base(syntax) => Statement = statement;
    }

    private sealed class NamespaceDeclarationGraph
    {
        public NamespaceDeclaration Declaration { get; }
        public NamespacePartGraph[] Parts { get; }
        public TopLevelDeclarationGraph[] Members { get; }
        public bool IsFileScoped { get; }

        public NamespaceDeclarationGraph(NamespaceDeclaration declaration, NamespacePartGraph[] parts, TopLevelDeclarationGraph[] members, bool isFileScoped)
        {
            Declaration = declaration;
            Parts = parts;
            Members = members;
            IsFileScoped = isFileScoped;
        }
    }

    private sealed class NamespacePartGraph
    {
        public SimpleName Syntax { get; }
        public SymbolName Name { get; }
        public NamespaceSymbol Symbol { get; }
        public Scope DeclaringScope { get; }
        public Scope Scope { get; }

        public NamespacePartGraph(SimpleName syntax, SymbolName name, NamespaceSymbol symbol, Scope declaringScope, Scope scope)
        {
            Syntax = syntax;
            Name = name;
            Symbol = symbol;
            DeclaringScope = declaringScope;
            Scope = scope;
        }
    }

    private sealed class TypeDeclarationGraph
    {
        public TypeDeclaration Declaration { get; }
        public TypeSymbol Symbol { get; }
        public Scope DeclaringScope { get; }
        public Scope Scope { get; }
        public MemberDeclarationGraph[] Members { get; }

        public TypeDeclarationGraph(TypeDeclaration declaration, TypeSymbol symbol, Scope declaringScope, Scope scope, MemberDeclarationGraph[] members)
        {
            Declaration = declaration;
            Symbol = symbol;
            DeclaringScope = declaringScope;
            Scope = scope;
            Members = members;
        }
    }

    private abstract class MemberDeclarationGraph
    {
        public Member Syntax { get; }

        protected MemberDeclarationGraph(Member syntax) => Syntax = syntax;
    }

    private sealed class TypeMemberDeclarationGraph : MemberDeclarationGraph
    {
        public MemberTypeDeclaration Wrapper { get; }
        public TypeDeclarationGraph Declaration { get; }

        public TypeMemberDeclarationGraph(MemberTypeDeclaration wrapper, TypeDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    private sealed class FunctionMemberDeclarationGraph : MemberDeclarationGraph
    {
        public MemberFunctionDeclaration Wrapper { get; }
        public FunctionDeclarationGraph Declaration { get; }

        public FunctionMemberDeclarationGraph(MemberFunctionDeclaration wrapper, FunctionDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    private sealed class VariableMemberDeclarationGraph : MemberDeclarationGraph
    {
        public MemberFieldDeclaration Wrapper { get; }
        public VariableDeclarationGraph Declaration { get; }

        public VariableMemberDeclarationGraph(MemberFieldDeclaration wrapper, VariableDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    private sealed class FunctionDeclarationGraph
    {
        public FunctionDeclaration Declaration { get; }
        public FunctionSymbol Symbol { get; }
        public Scope DeclaringScope { get; }
        public Scope Scope { get; }
        public FunctionBodyGraph Body { get; }

        public FunctionDeclarationGraph(FunctionDeclaration declaration, FunctionSymbol symbol, Scope declaringScope, Scope scope, FunctionBodyGraph body)
        {
            Declaration = declaration;
            Symbol = symbol;
            DeclaringScope = declaringScope;
            Scope = scope;
            Body = body;
        }
    }

    private abstract class FunctionBodyGraph
    {
        public FunctionBody Syntax { get; }

        protected FunctionBodyGraph(FunctionBody syntax) => Syntax = syntax;
    }

    private sealed class FunctionBlockBodyGraph : FunctionBodyGraph
    {
        public LocalDeclarationGraph[] Locals { get; }

        public FunctionBlockBodyGraph(FunctionBlockBody syntax, LocalDeclarationGraph[] locals)
            : base(syntax) => Locals = locals;
    }

    private sealed class FunctionLambdaBodyGraph : FunctionBodyGraph
    {
        public LocalStatementGraph Statement { get; }

        public FunctionLambdaBodyGraph(FunctionLambdaBody syntax, LocalStatementGraph statement)
            : base(syntax) => Statement = statement;
    }

    private sealed class FunctionEmptyBodyGraph : FunctionBodyGraph
    {
        public FunctionEmptyBodyGraph(FunctionEmptyBody syntax) : base(syntax) { }
    }

    private sealed class VariableDeclarationGraph
    {
        public VariableDeclaration Declaration { get; }
        public Scope DeclaringScope { get; }
        public VariableDeclaratorGraph[] Declarators { get; }

        public VariableDeclarationGraph(VariableDeclaration declaration, Scope declaringScope, VariableDeclaratorGraph[] declarators)
        {
            Declaration = declaration;
            DeclaringScope = declaringScope;
            Declarators = declarators;
        }
    }

    private sealed class VariableDeclaratorGraph
    {
        public VariableDeclarator Declarator { get; }
        public VariableSymbol Symbol { get; }

        public VariableDeclaratorGraph(VariableDeclarator declarator, VariableSymbol symbol)
        {
            Declarator = declarator;
            Symbol = symbol;
        }
    }

    private abstract class LocalDeclarationGraph
    {
        public Local Syntax { get; }

        protected LocalDeclarationGraph(Local syntax) => Syntax = syntax;
    }

    private sealed class TypeLocalDeclarationGraph : LocalDeclarationGraph
    {
        public LocalTypeDeclaration Wrapper { get; }
        public TypeDeclarationGraph Declaration { get; }

        public TypeLocalDeclarationGraph(LocalTypeDeclaration wrapper, TypeDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    private sealed class FunctionLocalDeclarationGraph : LocalDeclarationGraph
    {
        public LocalFunctionDeclaration Wrapper { get; }
        public FunctionDeclarationGraph Declaration { get; }

        public FunctionLocalDeclarationGraph(LocalFunctionDeclaration wrapper, FunctionDeclarationGraph declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    private sealed class StatementLocalDeclarationGraph : LocalDeclarationGraph
    {
        public LocalStatementGraph Statement { get; }

        public StatementLocalDeclarationGraph(LocalStatement syntax, LocalStatementGraph statement)
            : base(syntax) => Statement = statement;
    }

    private abstract class TopLevelStatementGraph
    {
        public TopLevelStatement Syntax { get; }

        protected TopLevelStatementGraph(TopLevelStatement syntax) => Syntax = syntax;
    }

    private sealed class TopLevelBlockStatementGraph : TopLevelStatementGraph
    {
        public Scope Scope { get; }
        public LocalDeclarationGraph[] Locals { get; }

        public TopLevelBlockStatementGraph(TopLevelBlockStatement syntax, Scope scope, LocalDeclarationGraph[] locals)
            : base(syntax)
        {
            Scope = scope;
            Locals = locals;
        }
    }

    private sealed class TopLevelIfStatementGraph : TopLevelStatementGraph
    {
        public TopLevelStatementGraph ThenStatement { get; }
        public TopLevelStatementGraph? ElseStatement { get; }

        public TopLevelIfStatementGraph(TopLevelIfStatement syntax, TopLevelStatementGraph thenStatement, TopLevelStatementGraph? elseStatement)
            : base(syntax)
        {
            ThenStatement = thenStatement;
            ElseStatement = elseStatement;
        }
    }

    private sealed class TopLevelWhileStatementGraph : TopLevelStatementGraph
    {
        public TopLevelStatementGraph Statement { get; }

        public TopLevelWhileStatementGraph(TopLevelWhileStatement syntax, TopLevelStatementGraph statement)
            : base(syntax) => Statement = statement;
    }

    private sealed class TopLevelElseStatementGraph : TopLevelStatementGraph
    {
        public TopLevelStatementGraph Statement { get; }

        public TopLevelElseStatementGraph(TopLevelElseStatement syntax, TopLevelStatementGraph statement)
            : base(syntax) => Statement = statement;
    }

    private sealed class SimpleTopLevelStatementGraph : TopLevelStatementGraph
    {
        public SimpleTopLevelStatementGraph(TopLevelStatement syntax) : base(syntax) { }
    }

    private abstract class LocalStatementGraph
    {
        public LocalStatement Syntax { get; }
        public Scope? Scope { get; }

        protected LocalStatementGraph(LocalStatement syntax, Scope? scope = null)
        {
            Syntax = syntax;
            Scope = scope;
        }
    }

    private sealed class LocalBlockStatementGraph : LocalStatementGraph
    {
        public new Scope Scope { get; }
        public LocalDeclarationGraph[] Locals { get; }

        public LocalBlockStatementGraph(LocalBlockStatement syntax, Scope scope, LocalDeclarationGraph[] locals)
            : base(syntax, scope)
        {
            Scope = scope;
            Locals = locals;
        }
    }

    private sealed class LocalIfStatementGraph : LocalStatementGraph
    {
        public LocalStatementGraph ThenStatement { get; }
        public LocalStatementGraph? ElseStatement { get; }

        public LocalIfStatementGraph(LocalIfStatement syntax, Scope? scope, LocalStatementGraph thenStatement, LocalStatementGraph? elseStatement)
            : base(syntax, scope)
        {
            ThenStatement = thenStatement;
            ElseStatement = elseStatement;
        }
    }

    private sealed class LocalWhileStatementGraph : LocalStatementGraph
    {
        public LocalStatementGraph Statement { get; }

        public LocalWhileStatementGraph(LocalWhileStatement syntax, Scope? scope, LocalStatementGraph statement)
            : base(syntax, scope) => Statement = statement;
    }

    private sealed class LocalElseStatementGraph : LocalStatementGraph
    {
        public LocalStatementGraph Statement { get; }

        public LocalElseStatementGraph(LocalElseStatement syntax, Scope? scope, LocalStatementGraph statement)
            : base(syntax, scope) => Statement = statement;
    }

    private sealed class LocalVariableStatementGraph : LocalStatementGraph
    {
        public VariableDeclarationGraph Declaration { get; }

        public LocalVariableStatementGraph(LocalVariableDeclarationStatement syntax, Scope? scope, VariableDeclarationGraph declaration)
            : base(syntax, scope) => Declaration = declaration;
    }

    private sealed class SimpleLocalStatementGraph : LocalStatementGraph
    {
        public SimpleLocalStatementGraph(LocalStatement syntax, Scope? scope = null) : base(syntax, scope) { }
    }

    private static SymbolName GetDeclaredName(NamedSyntax name) => name switch
    {
        SimpleName simpleName => SymbolName.FromToken(simpleName.Name),
        GenericName genericName => SymbolName.FromToken(genericName.Name),
        QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetDeclaredName(qualifiedName.Parts[^1]),
        _ => throw new InvalidOperationException($"Unhandled named syntax '{name.GetType().Name}'.")
    };

    private static int GetDeclaredArity(NamedSyntax name) => name switch
    {
        GenericName genericName => genericName.TypeParameters.Count,
        QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetDeclaredArity(qualifiedName.Parts[^1]),
        _ => 0
    };

    private static int CountSimpleNames(NamedSyntax name) => name switch
    {
        SimpleName => 1,
        QualifiedName qualifiedName => CountQualifiedParts(qualifiedName.Parts),
        _ => throw new InvalidOperationException($"Unhandled named syntax '{name.GetType().Name}'.")
    };

    private static int CountQualifiedParts(SeparatedSyntaxList<NamedSyntax> parts)
    {
        int count = 0;

        foreach (NamedSyntax name in parts)
            count += CountSimpleNames(name);

        return count;
    }
}
