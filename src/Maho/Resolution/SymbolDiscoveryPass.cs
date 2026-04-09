using System;
using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Symbols;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

/// <summary>
/// First semantic pass. Each compilation unit is collected into a unit-local declaration plan in
/// parallel, then those plans are merged into the shared project symbol graph.
/// </summary>
internal sealed class SymbolDiscoveryPass : ResolutionPass
{
    /// <summary>
    /// Symbol discovery is intentionally collect-then-merge so units can be scanned in parallel
    /// without racing on shared project scope dictionaries.
    /// </summary>
    public override ResolutionExecutionMode ExecutionMode => ResolutionExecutionMode.ParallelCollectThenMerge;

    /// <summary>
    /// Collects a unit-local declaration plan without mutating shared semantic state. The returned
    /// plan is merged later in a deterministic single-threaded phase.
    /// </summary>
    public override ResolutionPassUnitResult CollectUnit(ResolutionContext context) => new Collector(context.Root).Collect();

    /// <summary>
    /// Merges one unit's collected declaration plan into the real project scope/symbol graph.
    /// Merge runs after collection so shared state changes happen in one controlled place.
    /// </summary>
    public override void MergeUnit(ResolutionCoordinatorContext projectContext, ResolutionContext unitContext, ResolutionPassUnitResult? result)
    {
        if (result is not UnitPlan plan)
        {
            unitContext.Diagnostics.ReportResolutionStateError(default, $"unit plan '{unitContext.Root.GetType().Name}'");
            return;
        }

        new Merger(unitContext).Merge(plan);
    }

    /// <summary>
    /// Pure syntax walker that converts one compilation unit into an immutable-ish declaration plan.
    /// No semantic state is mutated here, which is what makes parallel collection safe.
    /// </summary>
    private sealed class Collector
    {
        /// <summary> Compilation unit being scanned into a declaration plan. </summary>
        private readonly CompilationUnit root;

        /// <summary> Creates a collector for one compilation unit. </summary>
        public Collector(CompilationUnit root) => this.root = root;

        /// <summary> Collects the full top-level declaration plan for the unit. </summary>
        public UnitPlan Collect() => new(root, CollectTopLevels(root.Members));

        /// <summary> Collects plans for every top-level syntax item in source order. </summary>
        private TopLevelPlan[] CollectTopLevels(IReadOnlyList<TopLevel> members)
        {
            TopLevelPlan[] plans = new TopLevelPlan[members.Count];

            for (int i = 0; i < members.Count; i++)
                plans[i] = CollectTopLevel(members[i]);

            return plans;
        }

        /// <summary> Converts one top-level syntax node into its corresponding plan shape. </summary>
        private TopLevelPlan CollectTopLevel(TopLevel topLevel) => topLevel switch
        {
            NamespaceDeclaration namespaceDeclaration => new NamespaceTopLevelPlan(namespaceDeclaration, CollectNamespace(namespaceDeclaration)),
            TopLevelTypeDeclaration typeDeclaration => new TypeTopLevelPlan(typeDeclaration, CollectTypeDeclaration(typeDeclaration.Type)),
            TopLevelFunctionDeclaration functionDeclaration => new FunctionTopLevelPlan(functionDeclaration, CollectFunctionDeclaration(functionDeclaration.Function)),
            TopLevelVariableDeclaration variableDeclaration => new VariableTopLevelPlan(variableDeclaration, CollectVariableDeclaration(variableDeclaration.Declaration)),
            TopLevelStatement statement => new StatementTopLevelPlan(statement, CollectTopLevelStatement(statement)),
            _ => throw new InvalidOperationException($"Unhandled top-level syntax '{topLevel.GetType().Name}'.")
        };

        /// <summary>
        /// Collects namespace structure without creating namespace symbols yet. Qualified namespace
        /// names are flattened into a list of simple parts so merge can replay them deterministically.
        /// </summary>
        private NamespacePlan CollectNamespace(NamespaceDeclaration declaration)
        {
            NamespacePartPlan[] parts = new NamespacePartPlan[CountSimpleNames(declaration.Name)];
            int partIndex = 0;
            CollectNamespaceParts(declaration.Name, parts, ref partIndex);

            return declaration.Body switch
            {
                NamespaceBlockBody blockBody => new NamespacePlan(declaration, parts, CollectTopLevels(blockBody.Members), isFileScoped: false),
                NamespaceEmptyBody => new NamespacePlan(declaration, parts, [], isFileScoped: true),
                _ => throw new InvalidOperationException($"Unhandled namespace body '{declaration.Body.GetType().Name}'.")
            };
        }

        /// <summary> Collects the declaration shape for one type, including member plans. </summary>
        private TypeDeclarationPlan CollectTypeDeclaration(TypeDeclaration declaration)
        {
            MemberPlan[] members = declaration.Body is TypeBlockBody blockBody
                ? CollectMembers(blockBody.Members)
                : [];

            return new TypeDeclarationPlan(
                declaration,
                GetDeclaredName(declaration.Name),
                GetDeclaredArity(declaration.Name),
                CollectTypeParameters(declaration.Name),
                members);
        }

        /// <summary> Collects member declaration plans in source order. </summary>
        private MemberPlan[] CollectMembers(IReadOnlyList<Member> members)
        {
            MemberPlan[] plans = new MemberPlan[members.Count];

            for (int i = 0; i < members.Count; i++)
                plans[i] = CollectMember(members[i]);

            return plans;
        }

        /// <summary> Converts one member syntax node into its corresponding declaration plan. </summary>
        private MemberPlan CollectMember(Member member) => member switch
        {
            MemberTypeDeclaration typeDeclaration => new TypeMemberPlan(typeDeclaration, CollectTypeDeclaration(typeDeclaration.Type)),
            MemberFunctionDeclaration functionDeclaration => new FunctionMemberPlan(functionDeclaration, CollectFunctionDeclaration(functionDeclaration.Function)),
            MemberFieldDeclaration fieldDeclaration => new VariableMemberPlan(fieldDeclaration, CollectVariableDeclaration(fieldDeclaration.Declaration)),
            _ => throw new InvalidOperationException($"Unhandled member syntax '{member.GetType().Name}'.")
        };

        /// <summary>
        /// Collects a function declaration shape, including type parameters, parameters, and a plan
        /// for whichever body form the function uses.
        /// </summary>
        private FunctionDeclarationPlan CollectFunctionDeclaration(FunctionDeclaration declaration)
        {
            FunctionBodyPlan body = declaration.Body switch
            {
                FunctionBlockBody blockBody => new FunctionBlockBodyPlan(blockBody, CollectLocals(blockBody.Locals)),
                FunctionLambdaBody lambdaBody => new FunctionLambdaBodyPlan(lambdaBody, CollectLocalStatement(lambdaBody.Statement)),
                FunctionEmptyBody emptyBody => new FunctionEmptyBodyPlan(emptyBody),
                _ => throw new InvalidOperationException($"Unhandled function body '{declaration.Body.GetType().Name}'.")
            };

            return new FunctionDeclarationPlan(
                declaration,
                GetDeclaredName(declaration.Signature.Identifier),
                GetDeclaredArity(declaration.Signature.Identifier),
                CollectTypeParameters(declaration.Signature.Identifier),
                CollectParameters(declaration.Signature.Parameters),
                body);
        }

        /// <summary> Collects generic type-parameter declarations from a name syntax when present. </summary>
        private static TypeParameterPlan[] CollectTypeParameters(NamedSyntax nameSyntax)
        {
            if (nameSyntax is not GenericName genericName)
                return [];

            TypeParameterPlan[] plans = new TypeParameterPlan[genericName.TypeParameters.Count];

            for (int i = 0; i < genericName.TypeParameters.Count; i++)
            {
                SimpleName typeParameterName = genericName.TypeParameters[i];
                plans[i] = new TypeParameterPlan(typeParameterName, SymbolName.FromToken(typeParameterName.Name), i);
            }

            return plans;
        }

        /// <summary> Collects parameter declarations and preserves their source order / ordinals. </summary>
        private static ParameterPlan[] CollectParameters(SeparatedSyntaxList<Parameter> parameters)
        {
            ParameterPlan[] plans = new ParameterPlan[parameters.Count];

            for (int i = 0; i < parameters.Count; i++)
            {
                Parameter parameter = parameters[i];
                plans[i] = new ParameterPlan(parameter, parameter.Declarator, GetDeclaredName(parameter.Declarator.Identifier), i);
            }

            return plans;
        }

        /// <summary> Collects one variable declaration into a plan per declarator. </summary>
        private static VariableDeclarationPlan CollectVariableDeclaration(VariableDeclaration declaration)
        {
            VariableDeclaratorPlan[] declarators = new VariableDeclaratorPlan[declaration.Declarators.Count];

            for (int i = 0; i < declaration.Declarators.Count; i++)
            {
                VariableDeclarator declarator = declaration.Declarators[i];
                declarators[i] = new VariableDeclaratorPlan(declarator, GetDeclaredName(declarator.Identifier));
            }

            return new VariableDeclarationPlan(declaration, declarators);
        }

        /// <summary> Collects local declaration plans in source order. </summary>
        private LocalPlan[] CollectLocals(IReadOnlyList<Local> locals)
        {
            LocalPlan[] plans = new LocalPlan[locals.Count];

            for (int i = 0; i < locals.Count; i++)
                plans[i] = CollectLocal(locals[i]);

            return plans;
        }

        /// <summary> Converts one local syntax node into its corresponding plan shape. </summary>
        private LocalPlan CollectLocal(Local local) => local switch
        {
            LocalTypeDeclaration typeDeclaration => new TypeLocalPlan(typeDeclaration, CollectTypeDeclaration(typeDeclaration.Type)),
            LocalFunctionDeclaration functionDeclaration => new FunctionLocalPlan(functionDeclaration, CollectFunctionDeclaration(functionDeclaration.Function)),
            LocalStatement statement => new StatementLocalPlan(statement, CollectLocalStatement(statement)),
            _ => throw new InvalidOperationException($"Unhandled local syntax '{local.GetType().Name}'.")
        };

        /// <summary> Collects the structural shape of a top-level statement subtree. </summary>
        private TopLevelStatementPlan CollectTopLevelStatement(TopLevelStatement statement) => statement switch
        {
            TopLevelBlockStatement blockStatement => new TopLevelBlockStatementPlan(blockStatement, CollectLocals(blockStatement.Locals)),
            TopLevelIfStatement ifStatement => new TopLevelIfStatementPlan(
                ifStatement,
                CollectTopLevelStatement(ifStatement.ThenStatement),
                ifStatement.ElseStatement is null ? null : CollectTopLevelStatement(ifStatement.ElseStatement.Statement)),
            TopLevelWhileStatement whileStatement => new TopLevelWhileStatementPlan(whileStatement, CollectTopLevelStatement(whileStatement.Statement)),
            TopLevelElseStatement elseStatement => new TopLevelElseStatementPlan(elseStatement, CollectTopLevelStatement(elseStatement.Statement)),
            TopLevelExpressionStatement expressionStatement => new SimpleTopLevelStatementPlan(expressionStatement),
            TopLevelReturnStatement returnStatement => new SimpleTopLevelStatementPlan(returnStatement),
            TopLevelEmptyStatement emptyStatement => new SimpleTopLevelStatementPlan(emptyStatement),
            _ => throw new InvalidOperationException($"Unhandled top-level statement '{statement.GetType().Name}'.")
        };

        /// <summary> Collects the structural shape of a local statement subtree. </summary>
        private LocalStatementPlan CollectLocalStatement(LocalStatement statement) => statement switch
        {
            LocalBlockStatement blockStatement => new LocalBlockStatementPlan(blockStatement, CollectLocals(blockStatement.Locals)),
            LocalIfStatement ifStatement => new LocalIfStatementPlan(
                ifStatement,
                CollectLocalStatement(ifStatement.ThenStatement),
                ifStatement.ElseStatement is null ? null : CollectLocalStatement(ifStatement.ElseStatement.Statement)),
            LocalWhileStatement whileStatement => new LocalWhileStatementPlan(whileStatement, CollectLocalStatement(whileStatement.Body)),
            LocalElseStatement elseStatement => new LocalElseStatementPlan(elseStatement, CollectLocalStatement(elseStatement.Statement)),
            LocalVariableDeclarationStatement variableDeclaration => new LocalVariableStatementPlan(variableDeclaration, CollectVariableDeclaration(variableDeclaration.Declaration)),
            LocalExpressionStatement expressionStatement => new SimpleLocalStatementPlan(expressionStatement),
            LocalReturnStatement returnStatement => new SimpleLocalStatementPlan(returnStatement),
            LocalEmptyStatement emptyStatement => new SimpleLocalStatementPlan(emptyStatement),
            _ => throw new InvalidOperationException($"Unhandled local statement '{statement.GetType().Name}'.")
        };
    }

    /// <summary>
    /// Semantic replayer that takes one unit plan and materializes the real symbols, scopes, and
    /// syntax associations into the mutable resolution context.
    /// </summary>
    private sealed class Merger
    {
        /// <summary> Unit-local semantic state being populated during merge. </summary>
        private readonly ResolutionContext context;
        /// <summary> Convenience projection of the shared diagnostics sink. </summary>
        private DiagnosticsManager Diagnostics => context.Diagnostics;

        /// <summary> Creates a merger for one unit context. </summary>
        public Merger(ResolutionContext context) => this.context = context;

        /// <summary>
        /// Replays the classic two-phase declaration pipeline against the collected plan so same-scope
        /// declarations exist before nested bodies are resolved.
        /// </summary>
        public void Merge(UnitPlan plan) => DeclareTopLevels(plan.TopLevels, context.GlobalScope, context.GlobalNamespace);

        /// <summary>
        /// First phase for top-level plans. This creates symbols/scopes before nested bodies are
        /// resolved so later declarations can already see same-scope containers.
        /// </summary>
        private void DeclareTopLevels(TopLevelPlan[] members, Scope scope, Symbol containerSymbol)
        {
            Scope currentScope = scope;
            Symbol currentContainerSymbol = containerSymbol;

            foreach (TopLevelPlan member in members)
            {
                DeclareTopLevel(member, currentScope, currentContainerSymbol);

                if (member is NamespaceTopLevelPlan { Namespace.IsFileScoped: true } namespacePlan)
                    (currentScope, currentContainerSymbol) = ResolveNamespaceContinuation(namespacePlan.Namespace, currentScope, currentContainerSymbol);
            }
        }

        private void DeclareTopLevel(TopLevelPlan topLevel, Scope scope, Symbol containerSymbol)
        {
            switch (topLevel)
            {
                case NamespaceTopLevelPlan namespacePlan:
                    DeclareNamespace(namespacePlan.Namespace, scope, containerSymbol);
                    break;

                case TypeTopLevelPlan typePlan:
                {
                    TypeSymbol symbol = DeclareTypeDeclaration(typePlan.Declaration, scope, containerSymbol);
                    context.ResolveDeclaredSymbol(typePlan.Wrapper, symbol);
                    break;
                }

                case FunctionTopLevelPlan functionPlan:
                {
                    FunctionSymbol symbol = DeclareFunctionDeclaration(functionPlan.Declaration, scope, containerSymbol);
                    context.ResolveDeclaredSymbol(functionPlan.Wrapper, symbol);
                    break;
                }

                case VariableTopLevelPlan variablePlan:
                    DeclareVariableDeclaration(variablePlan.Declaration, scope, containerSymbol);
                    break;

                case StatementTopLevelPlan statementPlan:
                    DeclareTopLevelStatement(statementPlan.Statement, scope, containerSymbol);
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled top-level plan '{topLevel.GetType().Name}'.");
            }
        }

        private void DeclareNamespace(NamespacePlan plan, Scope scope, Symbol parentSymbol)
        {
            Scope currentScope = scope;
            Symbol currentSymbol = parentSymbol;

            foreach (NamespacePartPlan part in plan.Parts)
            {
                NamespaceSymbol namespaceSymbol = GetOrDeclareNamespace(part, currentScope, currentSymbol);
                currentScope = context.ResolveSymbolScope(namespaceSymbol, part.Syntax, currentScope);
                currentSymbol = namespaceSymbol;
            }

            context.ResolveDeclaredSymbol(plan.Declaration, currentSymbol);
            context.ResolveScope(plan.Declaration, currentScope);
            context.ResolveScope(plan.Declaration.Body, currentScope);

            if (!plan.IsFileScoped)
                DeclareTopLevels(plan.Members, currentScope, currentSymbol);
        }

        private (Scope Scope, Symbol ContainerSymbol) ResolveNamespaceContinuation(NamespacePlan plan, Scope scope, Symbol parentSymbol)
        {
            Scope currentScope = scope;
            Symbol currentSymbol = parentSymbol;

            foreach (NamespacePartPlan part in plan.Parts)
            {
                currentSymbol = GetOrDeclareNamespace(part, currentScope, currentSymbol);
                currentScope = context.ResolveSymbolScope(currentSymbol, part.Syntax, currentScope);
            }

            return (currentScope, currentSymbol);
        }

        private NamespaceSymbol GetOrDeclareNamespace(NamespacePartPlan part, Scope scope, Symbol parentSymbol)
        {
            IReadOnlyList<Symbol> localSymbols = scope.LookupLocal(part.Name);

            for (int i = 0; i < localSymbols.Count; i++)
            {
                if (localSymbols[i] is not NamespaceSymbol namespaceSymbol)
                    continue;

                context.ResolveDeclaredSymbol(part.Syntax, namespaceSymbol);
                return namespaceSymbol;
            }

            NamespaceSymbol created = new(part.Name, parentSymbol, part.Syntax);
            context.DeclareSymbol(part.Syntax, created, scope);
            return created;
        }

        private TypeSymbol DeclareTypeDeclaration(TypeDeclarationPlan plan, Scope scope, Symbol parentSymbol)
        {
            TypeSymbol symbol = new(plan.Name, parentSymbol, plan.Declaration, plan.Arity);
            context.DeclareSymbol(plan.Declaration, symbol, scope);

            Scope typeScope = context.ResolveSymbolScope(symbol, plan.Declaration, scope);
            context.ResolveScope(plan.Declaration.Body, typeScope);

            TypeParameterSymbol[] typeParameters = DeclareTypeParameters(plan.TypeParameters, symbol, typeScope);
            symbol.ResolveTypeParameters(typeParameters);

            DeclareMembers(plan.Members, typeScope, symbol);
            return symbol;
        }

        private void DeclareMembers(MemberPlan[] members, Scope scope, Symbol containerSymbol)
        {
            foreach (MemberPlan member in members)
                DeclareMember(member, scope, containerSymbol);
        }

        private void DeclareMember(MemberPlan member, Scope scope, Symbol containerSymbol)
        {
            switch (member)
            {
                case TypeMemberPlan typePlan:
                {
                    TypeSymbol symbol = DeclareTypeDeclaration(typePlan.Declaration, scope, containerSymbol);
                    context.ResolveDeclaredSymbol(typePlan.Wrapper, symbol);
                    break;
                }

                case FunctionMemberPlan functionPlan:
                {
                    FunctionSymbol symbol = DeclareFunctionDeclaration(functionPlan.Declaration, scope, containerSymbol);
                    context.ResolveDeclaredSymbol(functionPlan.Wrapper, symbol);
                    break;
                }

                case VariableMemberPlan variablePlan:
                    DeclareVariableDeclaration(variablePlan.Declaration, scope, containerSymbol);
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled member plan '{member.GetType().Name}'.");
            }
        }

        private FunctionSymbol DeclareFunctionDeclaration(FunctionDeclarationPlan plan, Scope scope, Symbol parentSymbol)
        {
            FunctionSymbol symbol = new(plan.Name, parentSymbol, plan.Declaration, plan.Arity);
            context.DeclareSymbol(plan.Declaration, symbol, scope);

            Scope functionScope = context.ResolveSymbolScope(symbol, plan.Declaration, scope);
            context.ResolveDeclaredSymbol(plan.Declaration.Signature, symbol);
            context.ResolveScope(plan.Declaration.Signature, functionScope);
            context.ResolveScope(plan.Declaration.Body, functionScope);

            TypeParameterSymbol[] typeParameters = DeclareTypeParameters(plan.TypeParameters, symbol, functionScope);
            symbol.ResolveTypeParameters(typeParameters);

            ParameterSymbol[] parameters = DeclareParameters(plan.Parameters, functionScope, symbol);
            symbol.ResolveParameters(parameters);

            switch (plan.Body)
            {
                case FunctionBlockBodyPlan blockBody:
                    DeclareLocals(blockBody.Locals, functionScope, symbol);
                    break;

                case FunctionLambdaBodyPlan lambdaBody:
                    DeclareEmbeddedLocalStatement(lambdaBody.Statement, functionScope, symbol);
                    break;

                case FunctionEmptyBodyPlan:
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled function body plan '{plan.Body.GetType().Name}'.");
            }

            return symbol;
        }

        private TypeParameterSymbol[] DeclareTypeParameters(TypeParameterPlan[] typeParameters, Symbol ownerSymbol, Scope ownerScope)
        {
            TypeParameterSymbol[] symbols = new TypeParameterSymbol[typeParameters.Length];

            for (int i = 0; i < typeParameters.Length; i++)
            {
                TypeParameterPlan plan = typeParameters[i];
                TypeParameterSymbol symbol = new(plan.Name, ownerSymbol, plan.Syntax, plan.Ordinal);
                context.DeclareSymbol(plan.Syntax, symbol, ownerScope);
                symbols[i] = symbol;
            }

            return symbols;
        }

        private ParameterSymbol[] DeclareParameters(ParameterPlan[] parameters, Scope scope, Symbol functionSymbol)
        {
            ParameterSymbol[] resolvedParameters = new ParameterSymbol[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterPlan plan = parameters[i];
                ParameterSymbol symbol = new(plan.Name, functionSymbol, plan.Parameter, plan.Ordinal);

                context.DeclareSymbol(plan.Parameter, symbol, scope);
                context.ResolveDeclaredSymbol(plan.Declarator, symbol);
                resolvedParameters[i] = symbol;
            }

            return resolvedParameters;
        }

        private void DeclareVariableDeclaration(VariableDeclarationPlan plan, Scope scope, Symbol parentSymbol)
        {
            foreach (VariableDeclaratorPlan declarator in plan.Declarators)
            {
                VariableSymbol symbol = new(declarator.Name, parentSymbol, declarator.Declarator);
                context.DeclareSymbol(declarator.Declarator, symbol, scope);
            }
        }

        private void DeclareLocals(LocalPlan[] locals, Scope scope, Symbol containerSymbol)
        {
            foreach (LocalPlan local in locals)
                DeclareLocal(local, scope, containerSymbol);
        }

        private void DeclareLocal(LocalPlan local, Scope scope, Symbol containerSymbol)
        {
            switch (local)
            {
                case TypeLocalPlan typePlan:
                {
                    TypeSymbol symbol = DeclareTypeDeclaration(typePlan.Declaration, scope, containerSymbol);
                    context.ResolveDeclaredSymbol(typePlan.Wrapper, symbol);
                    break;
                }

                case FunctionLocalPlan functionPlan:
                {
                    FunctionSymbol symbol = DeclareFunctionDeclaration(functionPlan.Declaration, scope, containerSymbol);
                    context.ResolveDeclaredSymbol(functionPlan.Wrapper, symbol);
                    break;
                }

                case VariableLocalPlan variablePlan:
                    DeclareVariableDeclaration(variablePlan.Declaration, scope, containerSymbol);
                    break;

                case StatementLocalPlan statementPlan:
                    DeclareLocalStatement(statementPlan.Statement, scope, containerSymbol);
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled local plan '{local.GetType().Name}'.");
            }
        }

        private void DeclareTopLevelStatement(TopLevelStatementPlan statement, Scope scope, Symbol containerSymbol)
        {
            switch (statement)
            {
                case TopLevelBlockStatementPlan blockStatement:
                    DeclareLocalBlock(blockStatement.Syntax, blockStatement.Locals, scope, containerSymbol);
                    break;

                case TopLevelIfStatementPlan ifStatement:
                    DeclareTopLevelStatement(ifStatement.ThenStatement, scope, containerSymbol);

                    if (ifStatement.ElseStatement is not null)
                        DeclareTopLevelStatement(ifStatement.ElseStatement, scope, containerSymbol);
                    break;

                case TopLevelWhileStatementPlan whileStatement:
                    DeclareTopLevelStatement(whileStatement.Statement, scope, containerSymbol);
                    break;

                case TopLevelElseStatementPlan elseStatement:
                    DeclareTopLevelStatement(elseStatement.Statement, scope, containerSymbol);
                    break;

                case SimpleTopLevelStatementPlan:
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled top-level statement plan '{statement.GetType().Name}'.");
            }
        }

        private void DeclareLocalStatement(LocalStatementPlan statement, Scope scope, Symbol containerSymbol)
        {
            switch (statement)
            {
                case LocalBlockStatementPlan blockStatement:
                    DeclareLocalBlock(blockStatement.Syntax, blockStatement.Locals, scope, containerSymbol);
                    break;

                case LocalIfStatementPlan ifStatement:
                    DeclareEmbeddedLocalStatement(ifStatement.ThenStatement, scope, containerSymbol);

                    if (ifStatement.ElseStatement is not null)
                        DeclareEmbeddedLocalStatement(ifStatement.ElseStatement, scope, containerSymbol);
                    break;

                case LocalWhileStatementPlan whileStatement:
                    DeclareEmbeddedLocalStatement(whileStatement.Statement, scope, containerSymbol);
                    break;

                case LocalElseStatementPlan elseStatement:
                    DeclareEmbeddedLocalStatement(elseStatement.Statement, scope, containerSymbol);
                    break;

                case LocalVariableStatementPlan variableStatement:
                    DeclareVariableDeclaration(variableStatement.Declaration, scope, containerSymbol);
                    break;

                case SimpleLocalStatementPlan:
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled local statement plan '{statement.GetType().Name}'.");
            }
        }

        private void DeclareLocalBlock(SyntaxNode boundary, LocalPlan[] locals, Scope scope, Symbol containerSymbol)
        {
            Scope blockScope = context.CreateChildScope(boundary, scope);
            DeclareLocals(locals, blockScope, containerSymbol);
        }

        private void DeclareEmbeddedLocalStatement(LocalStatementPlan statement, Scope scope, Symbol containerSymbol)
        {
            if (statement is LocalBlockStatementPlan blockStatement)
            {
                DeclareLocalStatement(blockStatement, scope, containerSymbol);
                return;
            }

            Scope statementScope = context.CreateChildScope(statement.Syntax, scope);
            DeclareLocalStatement(statement, statementScope, containerSymbol);
        }
    }

    /// <summary>
    /// Full collected declaration shape for one compilation unit. This is the payload passed from
    /// the parallel collection phase into the sequential merge phase.
    /// </summary>
    private sealed class UnitPlan : ResolutionPassUnitResult
    {
        /// <summary> Original compilation unit the plan was collected from. </summary>
        public CompilationUnit Root { get; }
        /// <summary> Collected plans for every top-level syntax item in source order. </summary>
        public TopLevelPlan[] TopLevels { get; }

        /// <summary> Creates one unit-level declaration plan. </summary>
        public UnitPlan(CompilationUnit root, TopLevelPlan[] topLevels)
        {
            Root = root;
            TopLevels = topLevels;
        }
    }

    /// <summary> Base type for one collected top-level syntax item. </summary>
    private abstract class TopLevelPlan
    {
        /// <summary> Original syntax node this plan was collected from. </summary>
        public TopLevel Syntax { get; }

        protected TopLevelPlan(TopLevel syntax) => Syntax = syntax;
    }

    /// <summary> Collected plan for a top-level namespace declaration. </summary>
    private sealed class NamespaceTopLevelPlan : TopLevelPlan
    {
        /// <summary> Namespace declaration shape, including its qualified parts and nested members. </summary>
        public NamespacePlan Namespace { get; }

        public NamespaceTopLevelPlan(NamespaceDeclaration syntax, NamespacePlan @namespace)
            : base(syntax) => Namespace = @namespace;
    }

    /// <summary> Collected plan for a top-level type declaration wrapper. </summary>
    private sealed class TypeTopLevelPlan : TopLevelPlan
    {
        /// <summary> Wrapper node that appeared at top level in the parser tree. </summary>
        public TopLevelTypeDeclaration Wrapper { get; }
        /// <summary> Inner type declaration shape to materialize during merge. </summary>
        public TypeDeclarationPlan Declaration { get; }

        public TypeTopLevelPlan(TopLevelTypeDeclaration wrapper, TypeDeclarationPlan declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    /// <summary> Collected plan for a top-level function declaration wrapper. </summary>
    private sealed class FunctionTopLevelPlan : TopLevelPlan
    {
        /// <summary> Wrapper node that appeared at top level in the parser tree. </summary>
        public TopLevelFunctionDeclaration Wrapper { get; }
        /// <summary> Inner function declaration shape to materialize during merge. </summary>
        public FunctionDeclarationPlan Declaration { get; }

        public FunctionTopLevelPlan(TopLevelFunctionDeclaration wrapper, FunctionDeclarationPlan declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    /// <summary> Collected plan for a top-level variable declaration wrapper. </summary>
    private sealed class VariableTopLevelPlan : TopLevelPlan
    {
        /// <summary> Wrapper node that appeared at top level in the parser tree. </summary>
        public TopLevelVariableDeclaration Wrapper { get; }
        /// <summary> Variable declaration shape to materialize during merge. </summary>
        public VariableDeclarationPlan Declaration { get; }

        public VariableTopLevelPlan(TopLevelVariableDeclaration wrapper, VariableDeclarationPlan declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    /// <summary> Collected plan for a top-level statement subtree. </summary>
    private sealed class StatementTopLevelPlan : TopLevelPlan
    {
        /// <summary> Structural statement plan to replay during merge. </summary>
        public TopLevelStatementPlan Statement { get; }

        public StatementTopLevelPlan(TopLevelStatement syntax, TopLevelStatementPlan statement)
            : base(syntax) => Statement = statement;
    }

    /// <summary> Collected namespace declaration shape before any namespace symbols are created. </summary>
    private sealed class NamespacePlan
    {
        /// <summary> Original namespace declaration syntax. </summary>
        public NamespaceDeclaration Declaration { get; }
        /// <summary> Flattened qualified-name parts for the namespace chain. </summary>
        public NamespacePartPlan[] Parts { get; }
        /// <summary> Nested top-level plans when the namespace uses a block body. </summary>
        public TopLevelPlan[] Members { get; }
        /// <summary> True when the namespace uses file-scoped syntax and affects following top-level declarations. </summary>
        public bool IsFileScoped { get; }

        public NamespacePlan(NamespaceDeclaration declaration, NamespacePartPlan[] parts, TopLevelPlan[] members, bool isFileScoped)
        {
            Declaration = declaration;
            Parts = parts;
            Members = members;
            IsFileScoped = isFileScoped;
        }
    }

    /// <summary> One simple part of a namespace qualified-name chain. </summary>
    private sealed class NamespacePartPlan
    {
        /// <summary> Simple-name syntax node for this namespace part. </summary>
        public SimpleName Syntax { get; }
        /// <summary> Source-backed simple name used for lookup/creation during merge. </summary>
        public SymbolName Name { get; }

        public NamespacePartPlan(SimpleName syntax, SymbolName name)
        {
            Syntax = syntax;
            Name = name;
        }
    }

    /// <summary> Collected declaration shape for one type. </summary>
    private sealed class TypeDeclarationPlan
    {
        /// <summary> Original type declaration syntax. </summary>
        public TypeDeclaration Declaration { get; }
        /// <summary> Simple declared type name. </summary>
        public SymbolName Name { get; }
        /// <summary> Generic arity implied by the declaration syntax. </summary>
        public int Arity { get; }
        /// <summary> Collected type-parameter declarations. </summary>
        public TypeParameterPlan[] TypeParameters { get; }
        /// <summary> Collected member declaration plans. </summary>
        public MemberPlan[] Members { get; }

        public TypeDeclarationPlan(TypeDeclaration declaration, SymbolName name, int arity, TypeParameterPlan[] typeParameters, MemberPlan[] members)
        {
            Declaration = declaration;
            Name = name;
            Arity = arity;
            TypeParameters = typeParameters;
            Members = members;
        }
    }

    /// <summary> Base type for one collected member declaration plan. </summary>
    private abstract class MemberPlan
    {
        /// <summary> Original member syntax node this plan came from. </summary>
        public Member Syntax { get; }

        protected MemberPlan(Member syntax) => Syntax = syntax;
    }

    private sealed class TypeMemberPlan : MemberPlan
    {
        public MemberTypeDeclaration Wrapper { get; }
        public TypeDeclarationPlan Declaration { get; }

        public TypeMemberPlan(MemberTypeDeclaration wrapper, TypeDeclarationPlan declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    private sealed class FunctionMemberPlan : MemberPlan
    {
        public MemberFunctionDeclaration Wrapper { get; }
        public FunctionDeclarationPlan Declaration { get; }

        public FunctionMemberPlan(MemberFunctionDeclaration wrapper, FunctionDeclarationPlan declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    private sealed class VariableMemberPlan : MemberPlan
    {
        public MemberFieldDeclaration Wrapper { get; }
        public VariableDeclarationPlan Declaration { get; }

        public VariableMemberPlan(MemberFieldDeclaration wrapper, VariableDeclarationPlan declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    private sealed class FunctionDeclarationPlan
    {
        public FunctionDeclaration Declaration { get; }
        public SymbolName Name { get; }
        public int Arity { get; }
        public TypeParameterPlan[] TypeParameters { get; }
        public ParameterPlan[] Parameters { get; }
        public FunctionBodyPlan Body { get; }

        public FunctionDeclarationPlan(
            FunctionDeclaration declaration,
            SymbolName name,
            int arity,
            TypeParameterPlan[] typeParameters,
            ParameterPlan[] parameters,
            FunctionBodyPlan body)
        {
            Declaration = declaration;
            Name = name;
            Arity = arity;
            TypeParameters = typeParameters;
            Parameters = parameters;
            Body = body;
        }
    }

    private abstract class FunctionBodyPlan
    {
        public FunctionBody Syntax { get; }

        protected FunctionBodyPlan(FunctionBody syntax) => Syntax = syntax;
    }

    private sealed class FunctionBlockBodyPlan : FunctionBodyPlan
    {
        public LocalPlan[] Locals { get; }

        public FunctionBlockBodyPlan(FunctionBlockBody syntax, LocalPlan[] locals)
            : base(syntax) => Locals = locals;
    }

    private sealed class FunctionLambdaBodyPlan : FunctionBodyPlan
    {
        public LocalStatementPlan Statement { get; }

        public FunctionLambdaBodyPlan(FunctionLambdaBody syntax, LocalStatementPlan statement)
            : base(syntax) => Statement = statement;
    }

    private sealed class FunctionEmptyBodyPlan : FunctionBodyPlan
    {
        public FunctionEmptyBodyPlan(FunctionEmptyBody syntax) : base(syntax) { }
    }

    private sealed class TypeParameterPlan
    {
        public SimpleName Syntax { get; }
        public SymbolName Name { get; }
        public int Ordinal { get; }

        public TypeParameterPlan(SimpleName syntax, SymbolName name, int ordinal)
        {
            Syntax = syntax;
            Name = name;
            Ordinal = ordinal;
        }
    }

    private sealed class ParameterPlan
    {
        public Parameter Parameter { get; }
        public ParameterVariableDeclarator Declarator { get; }
        public SymbolName Name { get; }
        public int Ordinal { get; }

        public ParameterPlan(Parameter parameter, ParameterVariableDeclarator declarator, SymbolName name, int ordinal)
        {
            Parameter = parameter;
            Declarator = declarator;
            Name = name;
            Ordinal = ordinal;
        }
    }

    private sealed class VariableDeclarationPlan
    {
        public VariableDeclaration Declaration { get; }
        public VariableDeclaratorPlan[] Declarators { get; }

        public VariableDeclarationPlan(VariableDeclaration declaration, VariableDeclaratorPlan[] declarators)
        {
            Declaration = declaration;
            Declarators = declarators;
        }
    }

    private sealed class VariableDeclaratorPlan
    {
        public VariableDeclarator Declarator { get; }
        public SymbolName Name { get; }

        public VariableDeclaratorPlan(VariableDeclarator declarator, SymbolName name)
        {
            Declarator = declarator;
            Name = name;
        }
    }

    private abstract class LocalPlan
    {
        public Local Syntax { get; }

        protected LocalPlan(Local syntax) => Syntax = syntax;
    }

    private sealed class TypeLocalPlan : LocalPlan
    {
        public LocalTypeDeclaration Wrapper { get; }
        public TypeDeclarationPlan Declaration { get; }

        public TypeLocalPlan(LocalTypeDeclaration wrapper, TypeDeclarationPlan declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    private sealed class FunctionLocalPlan : LocalPlan
    {
        public LocalFunctionDeclaration Wrapper { get; }
        public FunctionDeclarationPlan Declaration { get; }

        public FunctionLocalPlan(LocalFunctionDeclaration wrapper, FunctionDeclarationPlan declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    private sealed class VariableLocalPlan : LocalPlan
    {
        public LocalVariableDeclarationStatement Wrapper { get; }
        public VariableDeclarationPlan Declaration { get; }

        public VariableLocalPlan(LocalVariableDeclarationStatement wrapper, VariableDeclarationPlan declaration)
            : base(wrapper)
        {
            Wrapper = wrapper;
            Declaration = declaration;
        }
    }

    private sealed class StatementLocalPlan : LocalPlan
    {
        public LocalStatementPlan Statement { get; }

        public StatementLocalPlan(LocalStatement syntax, LocalStatementPlan statement)
            : base(syntax) => Statement = statement;
    }

    private abstract class TopLevelStatementPlan
    {
        public TopLevelStatement Syntax { get; }

        protected TopLevelStatementPlan(TopLevelStatement syntax) => Syntax = syntax;
    }

    private sealed class TopLevelBlockStatementPlan : TopLevelStatementPlan
    {
        public LocalPlan[] Locals { get; }

        public TopLevelBlockStatementPlan(TopLevelBlockStatement syntax, LocalPlan[] locals)
            : base(syntax) => Locals = locals;
    }

    private sealed class TopLevelIfStatementPlan : TopLevelStatementPlan
    {
        public TopLevelStatementPlan ThenStatement { get; }
        public TopLevelStatementPlan? ElseStatement { get; }

        public TopLevelIfStatementPlan(TopLevelIfStatement syntax, TopLevelStatementPlan thenStatement, TopLevelStatementPlan? elseStatement)
            : base(syntax)
        {
            ThenStatement = thenStatement;
            ElseStatement = elseStatement;
        }
    }

    private sealed class TopLevelWhileStatementPlan : TopLevelStatementPlan
    {
        public TopLevelStatementPlan Statement { get; }

        public TopLevelWhileStatementPlan(TopLevelWhileStatement syntax, TopLevelStatementPlan statement)
            : base(syntax) => Statement = statement;
    }

    private sealed class TopLevelElseStatementPlan : TopLevelStatementPlan
    {
        public TopLevelStatementPlan Statement { get; }

        public TopLevelElseStatementPlan(TopLevelElseStatement syntax, TopLevelStatementPlan statement)
            : base(syntax) => Statement = statement;
    }

    private sealed class SimpleTopLevelStatementPlan : TopLevelStatementPlan
    {
        public SimpleTopLevelStatementPlan(TopLevelStatement syntax) : base(syntax) { }
    }

    private abstract class LocalStatementPlan
    {
        public LocalStatement Syntax { get; }

        protected LocalStatementPlan(LocalStatement syntax) => Syntax = syntax;
    }

    private sealed class LocalBlockStatementPlan : LocalStatementPlan
    {
        public LocalPlan[] Locals { get; }

        public LocalBlockStatementPlan(LocalBlockStatement syntax, LocalPlan[] locals)
            : base(syntax) => Locals = locals;
    }

    private sealed class LocalIfStatementPlan : LocalStatementPlan
    {
        public LocalStatementPlan ThenStatement { get; }
        public LocalStatementPlan? ElseStatement { get; }

        public LocalIfStatementPlan(LocalIfStatement syntax, LocalStatementPlan thenStatement, LocalStatementPlan? elseStatement)
            : base(syntax)
        {
            ThenStatement = thenStatement;
            ElseStatement = elseStatement;
        }
    }

    private sealed class LocalWhileStatementPlan : LocalStatementPlan
    {
        public LocalStatementPlan Statement { get; }

        public LocalWhileStatementPlan(LocalWhileStatement syntax, LocalStatementPlan statement)
            : base(syntax) => Statement = statement;
    }

    private sealed class LocalElseStatementPlan : LocalStatementPlan
    {
        public LocalStatementPlan Statement { get; }

        public LocalElseStatementPlan(LocalElseStatement syntax, LocalStatementPlan statement)
            : base(syntax) => Statement = statement;
    }

    private sealed class LocalVariableStatementPlan : LocalStatementPlan
    {
        public VariableDeclarationPlan Declaration { get; }

        public LocalVariableStatementPlan(LocalVariableDeclarationStatement syntax, VariableDeclarationPlan declaration)
            : base(syntax) => Declaration = declaration;
    }

    private sealed class SimpleLocalStatementPlan : LocalStatementPlan
    {
        public SimpleLocalStatementPlan(LocalStatement syntax) : base(syntax) { }
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

        for (int i = 0; i < parts.Count; i++)
            count += CountSimpleNames(parts[i]);

        return count;
    }

    private static void CollectNamespaceParts(NamedSyntax name, NamespacePartPlan[] parts, ref int partIndex)
    {
        switch (name)
        {
            case SimpleName simpleName:
                parts[partIndex++] = new NamespacePartPlan(simpleName, SymbolName.FromToken(simpleName.Name));
                return;

            case QualifiedName qualifiedName:
                for (int i = 0; i < qualifiedName.Parts.Count; i++)
                    CollectNamespaceParts(qualifiedName.Parts[i], parts, ref partIndex);
                return;

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
            GetNamedSyntaxSpan(qualifiedName.Parts[^1]).End),
        _ => default
    };

    /// <summary>
    /// Finds the source buffer backing one name syntax so project-wide diagnostics emitted during
    /// merge can still be attached to the correct compilation unit.
    /// </summary>
    private static SourceText? GetNamedSyntaxSource(NamedSyntax name) => name switch
    {
        SimpleName simpleName => simpleName.Name.Source,
        GenericName genericName => genericName.Name.Source,
        QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetNamedSyntaxSource(qualifiedName.Parts[0]),
        _ => null
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

    /// <summary>
    /// Finds the source buffer for syntax that may surface structural-resolution diagnostics after
    /// the project-wide merge phase has already decoupled work from the original file loop.
    /// </summary>
    private static SourceText? GetSyntaxSource(SyntaxNode syntax) => syntax switch
    {
        TypeDeclaration typeDeclaration => typeDeclaration.Keyword.Source,
        NamespaceDeclaration namespaceDeclaration => namespaceDeclaration.Keyword.Source,
        FunctionDeclaration functionDeclaration => GetNamedSyntaxSource(functionDeclaration.Signature.Identifier),
        LocalBlockStatement blockStatement => blockStatement.OpenBrace.Source,
        TopLevelBlockStatement blockStatement => blockStatement.OpenBrace.Source,
        LocalStatement localStatement => GetLocalStatementSource(localStatement),
        TopLevelStatement topLevelStatement => GetTopLevelStatementSource(topLevelStatement),
        _ => null
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

    private static SourceText? GetLocalStatementSource(LocalStatement statement) => statement switch
    {
        LocalIfStatement ifStatement => ifStatement.Keyword.Source,
        LocalWhileStatement whileStatement => whileStatement.Keyword.Source,
        LocalElseStatement elseStatement => elseStatement.Keyword.Source,
        LocalVariableDeclarationStatement variableDeclaration => GetNamedSyntaxSource(variableDeclaration.Declaration.Declarators[0].Identifier),
        LocalExpressionStatement expressionStatement => expressionStatement.Semicolon.Source,
        LocalReturnStatement returnStatement => returnStatement.Statement.Keyword.Source,
        LocalEmptyStatement emptyStatement => emptyStatement.Semicolon.Source,
        LocalBlockStatement blockStatement => blockStatement.OpenBrace.Source,
        _ => null
    };

    private static SourceText? GetTopLevelStatementSource(TopLevelStatement statement) => statement switch
    {
        TopLevelIfStatement ifStatement => ifStatement.Keyword.Source,
        TopLevelWhileStatement whileStatement => whileStatement.Keyword.Source,
        TopLevelElseStatement elseStatement => elseStatement.Keyword.Source,
        TopLevelExpressionStatement expressionStatement => expressionStatement.Semicolon.Source,
        TopLevelReturnStatement returnStatement => returnStatement.Statement.Keyword.Source,
        TopLevelEmptyStatement emptyStatement => emptyStatement.Semicolon.Source,
        TopLevelBlockStatement blockStatement => blockStatement.OpenBrace.Source,
        _ => null
    };
}
