using System;
using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

/// <summary>
/// Resolves directly declared type-hierarchy edges after symbol discovery has produced the canonical
/// project-wide symbol graph. Each unit resolves its own base-type references in parallel, then the
/// pass performs one sequential project-wide cycle check.
/// </summary>
internal sealed class TypeHierarchyResolutionPass : ResolutionPass
{
    /// <summary>
    /// Once symbol discovery has finished, hierarchy lookup only reads shared project state and
    /// writes to unit-local maps plus each declaration's own type symbol.
    /// </summary>
    public override ResolutionExecutionMode ExecutionMode => ResolutionExecutionMode.ParallelUnitLocal;

    /// <summary> Resolves direct hierarchy edges for every type declared inside one compilation unit. </summary>
    public override void ExecuteUnit(ResolutionContext context) => new UnitResolver(context).Execute();

    /// <summary> Performs a project-wide cycle check after every unit has resolved its direct edges. </summary>
    public override void AfterProject(ResolutionCoordinatorContext context) => DetectCycles(context);

    /// <summary> Performs one unit's direct type-hierarchy resolution. </summary>
    private sealed class UnitResolver
    {
        private readonly ResolutionContext context;

        /// <summary> Creates the helper that resolves one compilation unit against shared project state. </summary>
        public UnitResolver(ResolutionContext context) => this.context = context;

        /// <summary> Starts the unit walk from the compilation unit root. </summary>
        public void Execute() => VisitTopLevels(context.Root.Members);

        /// <summary> Visits every top-level item in source order. </summary>
        private void VisitTopLevels(IReadOnlyList<TopLevel> members)
        {
            foreach (TopLevel member in members)
                VisitTopLevel(member);
        }

        /// <summary> Dispatches one top-level item to the traversal needed for hierarchy resolution. </summary>
        private void VisitTopLevel(TopLevel member)
        {
            switch (member)
            {
                case NamespaceDeclaration namespaceDeclaration when namespaceDeclaration.Body is NamespaceBlockBody blockBody:
                    VisitTopLevels(blockBody.Members);
                    break;

                case TopLevelTypeDeclaration typeDeclaration:
                    VisitTypeDeclaration(typeDeclaration.Type);
                    break;

                case TopLevelFunctionDeclaration functionDeclaration:
                    VisitFunctionDeclaration(functionDeclaration.Function);
                    break;

                case TopLevelVariableDeclaration:
                case TopLevelExpressionStatement:
                case TopLevelReturnStatement:
                case TopLevelEmptyStatement:
                    break;

                case TopLevelBlockStatement blockStatement:
                    VisitLocals(blockStatement.Locals);
                    break;

                case TopLevelIfStatement ifStatement:
                    VisitTopLevelStatement(ifStatement.ThenStatement);

                    if (ifStatement.ElseStatement is not null)
                        VisitTopLevelStatement(ifStatement.ElseStatement);

                    break;

                case TopLevelWhileStatement whileStatement:
                    VisitTopLevelStatement(whileStatement.Statement);
                    break;

                case TopLevelElseStatement elseStatement:
                    VisitTopLevelStatement(elseStatement.Statement);
                    break;
            }
        }

        /// <summary> Traverses nested top-level statements so local type declarations still participate in hierarchy binding. </summary>
        private void VisitTopLevelStatement(TopLevelStatement statement)
        {
            switch (statement)
            {
                case TopLevelBlockStatement blockStatement:
                    VisitLocals(blockStatement.Locals);
                    break;

                case TopLevelIfStatement ifStatement:
                    VisitTopLevelStatement(ifStatement.ThenStatement);

                    if (ifStatement.ElseStatement is not null)
                        VisitTopLevelStatement(ifStatement.ElseStatement);

                    break;

                case TopLevelWhileStatement whileStatement:
                    VisitTopLevelStatement(whileStatement.Statement);
                    break;

                case TopLevelElseStatement elseStatement:
                    VisitTopLevelStatement(elseStatement.Statement);
                    break;
            }
        }

        /// <summary> Traverses a function body because local declarations can introduce nested types. </summary>
        private void VisitFunctionDeclaration(FunctionDeclaration declaration)
        {
            switch (declaration.Body)
            {
                case FunctionBlockBody blockBody:
                    VisitLocals(blockBody.Locals);
                    break;

                case FunctionLambdaBody lambdaBody:
                    VisitLocalStatement(lambdaBody.Statement);
                    break;
            }
        }

        /// <summary> Visits all local items inside one lexical container. </summary>
        private void VisitLocals(IReadOnlyList<Local> locals)
        {
            foreach (Local local in locals)
                VisitLocal(local);
        }

        /// <summary> Dispatches one local declaration or statement to the traversal needed for type discovery. </summary>
        private void VisitLocal(Local local)
        {
            switch (local)
            {
                case LocalTypeDeclaration typeDeclaration:
                    VisitTypeDeclaration(typeDeclaration.Type);
                    break;

                case LocalFunctionDeclaration functionDeclaration:
                    VisitFunctionDeclaration(functionDeclaration.Function);
                    break;

                case LocalStatement statement:
                    VisitLocalStatement(statement);
                    break;
            }
        }

        /// <summary> Traverses nested local statements so embedded local types are not skipped. </summary>
        private void VisitLocalStatement(LocalStatement statement)
        {
            switch (statement)
            {
                case LocalBlockStatement blockStatement:
                    VisitLocals(blockStatement.Locals);
                    break;

                case LocalIfStatement ifStatement:
                    VisitLocalStatement(ifStatement.ThenStatement);

                    if (ifStatement.ElseStatement is not null)
                        VisitLocalStatement(ifStatement.ElseStatement);

                    break;

                case LocalWhileStatement whileStatement:
                    VisitLocalStatement(whileStatement.Body);
                    break;

                case LocalElseStatement elseStatement:
                    VisitLocalStatement(elseStatement.Statement);
                    break;
            }
        }

        /// <summary> Resolves one declared type's direct base edges, then recurses into nested declarations. </summary>
        private void VisitTypeDeclaration(TypeDeclaration declaration)
        {
            if (!context.TryResolveDeclaredSymbol(declaration, out Symbol? declaredSymbol) || declaredSymbol is not TypeSymbol typeSymbol)
            {
                context.Diagnostics.ReportResolutionStateError(
                    declaration.GetSpan() ?? default,
                    $"type declaration '{GetDeclaredName(declaration.Name)}'",
                    declaration.GetSource());
                return;
            }

            ResolveDirectBaseTypes(declaration, typeSymbol);

            if (declaration.Body is not TypeBlockBody blockBody)
                return;

            foreach (Member member in blockBody.Members)
                VisitMember(member);
        }

        /// <summary> Traverses type members that can themselves contain nested type declarations. </summary>
        private void VisitMember(Member member)
        {
            switch (member)
            {
                case MemberTypeDeclaration typeDeclaration:
                    VisitTypeDeclaration(typeDeclaration.Type);
                    break;

                case MemberFunctionDeclaration functionDeclaration:
                    VisitFunctionDeclaration(functionDeclaration.Function);
                    break;
            }
        }

        /// <summary> Resolves the direct base-type list for one type declaration and stores it on the symbol. </summary>
        private void ResolveDirectBaseTypes(TypeDeclaration declaration, TypeSymbol typeSymbol)
        {
            if (declaration.Base is null)
            {
                typeSymbol.ResolveBaseTypes([]);
                return;
            }

            if (!context.TryResolveSymbolScope(typeSymbol, out Scope? typeScope) || typeScope is null)
            {
                context.Diagnostics.ReportResolutionStateError(
                    declaration.GetSpan() ?? default,
                    $"type scope '{typeSymbol.Name}'",
                    declaration.GetSource());
                typeSymbol.ResolveBaseTypes([]);
                return;
            }

            List<TypeSymbol> resolvedBaseTypes = [];

            foreach (var baseTypeSyntax in declaration.Base.BaseTypes)
            {
                ResolvedTypeReference resolvedReference = ResolveTypeReference(baseTypeSyntax, typeScope);
                TypeSymbol? resolvedBaseType = ResolveUniqueBaseType(resolvedReference);

                if (resolvedBaseType is not null)
                    resolvedBaseTypes.Add(resolvedBaseType);
            }

            typeSymbol.ResolveBaseTypes([.. resolvedBaseTypes]);
        }

        /// <summary> Resolves one type-syntax occurrence and records the semantic result in the unit map. </summary>
        private ResolvedTypeReference ResolveTypeReference(TypeSyntax syntax, Scope scope)
        {
            ResolvedTypeReference resolved = ResolveTypeReferenceCore(syntax, scope, [scope], lexicalLookup: true);
            context.ResolveTypeReference(syntax, resolved);
            return resolved;
        }

        /// <summary> Resolves any supported type-syntax form under the provided lookup rules. </summary>
        private ResolvedTypeReference ResolveTypeReferenceCore(TypeSyntax syntax, Scope lexicalScope, Scope[] scopes, bool lexicalLookup)
        {
            return syntax switch
            {
                SimpleType simpleType => ResolveNamedType(simpleType, simpleType.Name.Value, arity: 0, [], LookupCandidates(scopes, SymbolName.FromToken(simpleType.Name), 0, lexicalLookup, allowNamespaces: true, allowTypeParameters: true)),
                GenericType genericType => ResolveGenericType(genericType, lexicalScope, scopes, lexicalLookup),
                QualifiedType qualifiedType => ResolveQualifiedType(qualifiedType, lexicalScope, scopes, lexicalLookup),
                ModifiedType modifiedType => ResolveModifiedType(modifiedType, lexicalScope, scopes, lexicalLookup),
                _ => throw new InvalidOperationException($"Unhandled type syntax '{syntax.GetType().Name}'.")
            };
        }

        /// <summary> Resolves a generic type reference, including all type arguments, under the current scope. </summary>
        private ResolvedNamedTypeReference ResolveGenericType(GenericType syntax, Scope lexicalScope, Scope[] scopes, bool lexicalLookup)
        {
            ResolvedTypeReference[] typeArguments = new ResolvedTypeReference[syntax.TypeArguments.Count];

            for (int i = 0; i < syntax.TypeArguments.Count; i++)
                typeArguments[i] = ResolveTypeReference(syntax.TypeArguments[i], lexicalScope);

            Symbol[] candidates = LookupCandidates(scopes, SymbolName.FromToken(syntax.Name), syntax.TypeArguments.Count, lexicalLookup, allowNamespaces: false, allowTypeParameters: false);
            return ResolveNamedType(syntax, syntax.Name.Value, syntax.TypeArguments.Count, typeArguments, candidates);
        }

        /// <summary> Resolves a qualified type reference by treating the left side as a container for the right. </summary>
        private ResolvedQualifiedTypeReference ResolveQualifiedType(QualifiedType syntax, Scope lexicalScope, Scope[] scopes, bool lexicalLookup)
        {
            ResolvedTypeReference left = ResolveTypeReferenceCore(syntax.Left, lexicalScope, scopes, lexicalLookup);
            Scope[] candidateScopes = CollectCandidateScopes(left);
            ResolvedTypeReference right = ResolveTypeReferenceCore(syntax.Right, lexicalScope, candidateScopes, lexicalLookup: false);
            return new ResolvedQualifiedTypeReference(syntax, left, right, [.. right.CandidateSymbols]);
        }

        /// <summary> Resolves the underlying element type, then reapplies any postfix type modifier. </summary>
        private ResolvedTypeReference ResolveModifiedType(ModifiedType syntax, Scope lexicalScope, Scope[] scopes, bool lexicalLookup)
        {
            ResolvedTypeReference elementType = ResolveTypeReferenceCore(syntax.Type, lexicalScope, scopes, lexicalLookup);
            return syntax.Modifier is null
                ? elementType
                : new ResolvedModifiedTypeReference(syntax, elementType, syntax.Modifier);
        }

        /// <summary> Creates the canonical semantic object for one simple or generic named type reference. </summary>
        private static ResolvedNamedTypeReference ResolveNamedType(TypeSyntax syntax, string name, int arity, ResolvedTypeReference[] typeArguments, Symbol[] candidates) =>
            new ResolvedNamedTypeReference(syntax, name, arity, typeArguments, candidates, CreateExplicitSignatureKey(candidates, typeArguments));

        /// <summary> Collects the member scopes owned by the left side of a qualified type reference. </summary>
        private Scope[] CollectCandidateScopes(ResolvedTypeReference left)
        {
            List<Scope> scopes = [];
            HashSet<Scope> seen = new(ReferenceEqualityComparer.Instance);

            foreach (Symbol candidate in left.CandidateSymbols)
            {
                if (!context.TryResolveSymbolScope(candidate, out Scope? scope) || scope is null || !seen.Add(scope))
                    continue;

                scopes.Add(scope);
            }

            return [.. scopes];
        }

        /// <summary> Looks up matching candidates in the provided scopes while filtering by symbol kind and arity. </summary>
        private static Symbol[] LookupCandidates(Scope[] scopes, SymbolName name, int arity, bool lexicalLookup, bool allowNamespaces, bool allowTypeParameters)
        {
            List<Symbol> matches = [];
            HashSet<Symbol> seen = new(ReferenceEqualityComparer.Instance);

            foreach (Scope scope in scopes)
            {
                IEnumerable<Symbol> symbols = lexicalLookup ? scope.Lookup(name) : scope.LookupLocal(name);

                foreach (Symbol symbol in symbols)
                {
                    if (!IsCandidateMatch(symbol, arity, allowNamespaces, allowTypeParameters) || !seen.Add(symbol))
                        continue;

                    matches.Add(symbol);
                }
            }

            return [.. matches];
        }

        /// <summary> Tests whether one looked-up symbol is a legal candidate for the current type reference shape. </summary>
        private static bool IsCandidateMatch(Symbol symbol, int arity, bool allowNamespaces, bool allowTypeParameters) => symbol switch
        {
            NamespaceSymbol => allowNamespaces && arity == 0,
            TypeParameterSymbol => allowTypeParameters && arity == 0,
            TypeSymbol typeSymbol => typeSymbol.Arity == arity,
            _ => false
        };

        /// <summary> Prefers an exact semantic signature when lookup found one unambiguous target symbol. </summary>
        private static string? CreateExplicitSignatureKey(Symbol[] candidates, ReadOnlySpan<ResolvedTypeReference> typeArguments)
        {
            if (candidates.Length != 1)
                return null;

            return candidates[0] switch
            {
                TypeParameterSymbol typeParameterSymbol when typeArguments.Length == 0 => typeParameterSymbol.SignatureIdentity,
                TypeSymbol typeSymbol when typeArguments.Length == 0 => typeSymbol.QualifiedMetadataName,
                TypeSymbol typeSymbol => $"{typeSymbol.QualifiedMetadataName}<{JoinSignatureKeys(typeArguments)}>",
                _ => null
            };
        }

        /// <summary> Joins already-resolved type-argument signatures into one normalized argument list. </summary>
        private static string JoinSignatureKeys(ReadOnlySpan<ResolvedTypeReference> typeArguments)
        {
            string[] parts = new string[typeArguments.Length];

            for (int i = 0; i < typeArguments.Length; i++)
                parts[i] = typeArguments[i].SignatureKey;

            return string.Join(",", parts);
        }

        /// <summary> Reduces a resolved reference to one unique direct base type while reporting lookup failures. </summary>
        private TypeSymbol? ResolveUniqueBaseType(ResolvedTypeReference resolvedReference)
        {
            TypeSymbol? singleType = null;

            foreach (Symbol candidate in resolvedReference.CandidateSymbols)
            {
                if (candidate is not TypeSymbol typeCandidate)
                    continue;

                if (singleType is not null && !ReferenceEquals(singleType, typeCandidate))
                {
                    ReportAmbiguousBaseType(resolvedReference);
                    return null;
                }

                singleType = typeCandidate;
            }

            if (singleType is not null)
                return singleType;

            ReportUnresolvedBaseType(resolvedReference);
            return null;
        }

        /// <summary> Reports that one declared base-type syntax resolved to no concrete type symbol. </summary>
        private void ReportUnresolvedBaseType(ResolvedTypeReference resolvedReference)
        {
            TextSpan span = resolvedReference.Syntax.GetSpan() ?? default;
            context.Diagnostics.ReportUnresolvedTypeReference(span, resolvedReference.DisplayName, resolvedReference.Syntax.GetSource());
        }

        /// <summary> Reports that one declared base-type syntax matched more than one concrete type symbol. </summary>
        private void ReportAmbiguousBaseType(ResolvedTypeReference resolvedReference)
        {
            TextSpan span = resolvedReference.Syntax.GetSpan() ?? default;
            context.Diagnostics.ReportAmbiguousTypeReference(span, resolvedReference.DisplayName, resolvedReference.Syntax.GetSource());
        }
    }

    /// <summary> Sequential project-wide cycle detection over the direct hierarchy edges resolved earlier. </summary>
    private static void DetectCycles(ResolutionCoordinatorContext context)
    {
        List<TypeSymbol> types = CollectDeclaredTypes(context);
        Dictionary<TypeSymbol, VisitState> states = new(ReferenceEqualityComparer.Instance);
        List<TypeSymbol> path = [];
        HashSet<TypeSymbol> reported = new(ReferenceEqualityComparer.Instance);

        foreach (TypeSymbol type in types)
        {
            if (!states.TryGetValue(type, out VisitState state) || state == VisitState.NotVisited)
                Visit(type);
        }

        void Visit(TypeSymbol type)
        {
            states[type] = VisitState.Visiting;
            path.Add(type);

            foreach (TypeSymbol baseType in type.BaseTypes)
            {
                if (states.TryGetValue(baseType, out VisitState state))
                {
                    if (state == VisitState.Visiting)
                        ReportCycleFrom(baseType);

                    continue;
                }

                Visit(baseType);
            }

            path.RemoveAt(path.Count - 1);
            states[type] = VisitState.Visited;
        }

        void ReportCycleFrom(TypeSymbol cycleStart)
        {
            int cycleStartIndex = path.IndexOf(cycleStart);

            if (cycleStartIndex < 0)
                return;

            for (int i = cycleStartIndex; i < path.Count; i++)
            {
                TypeSymbol cycleType = path[i];

                if (!reported.Add(cycleType))
                    continue;

                ReportCycleDiagnostic(context, cycleType);
            }
        }
    }

    /// <summary> Gathers every declared type symbol exactly once across all units. </summary>
    private static List<TypeSymbol> CollectDeclaredTypes(ResolutionCoordinatorContext context)
    {
        List<TypeSymbol> types = [];
        HashSet<TypeSymbol> seen = new(ReferenceEqualityComparer.Instance);

        foreach (ResolutionContext unit in context.Units)
            CollectDeclaredTypes(unit.Root.Members, unit, types, seen);

        return types;
    }

    /// <summary> Collects every declared type reachable from one top-level member list. </summary>
    private static void CollectDeclaredTypes(IReadOnlyList<TopLevel> members, ResolutionContext context, List<TypeSymbol> types, HashSet<TypeSymbol> seen)
    {
        foreach (TopLevel member in members)
        {
            switch (member)
            {
                case NamespaceDeclaration namespaceDeclaration when namespaceDeclaration.Body is NamespaceBlockBody blockBody:
                    CollectDeclaredTypes(blockBody.Members, context, types, seen);
                    break;

                case TopLevelTypeDeclaration typeDeclaration:
                    CollectDeclaredType(typeDeclaration.Type, context, types, seen);
                    break;

                case TopLevelFunctionDeclaration functionDeclaration:
                    CollectDeclaredTypes(functionDeclaration.Function, context, types, seen);
                    break;

                case TopLevelBlockStatement blockStatement:
                    CollectDeclaredTypes(blockStatement.Locals, context, types, seen);
                    break;

                case TopLevelIfStatement ifStatement:
                    CollectDeclaredTypes(ifStatement.ThenStatement, context, types, seen);

                    if (ifStatement.ElseStatement is not null)
                        CollectDeclaredTypes(ifStatement.ElseStatement, context, types, seen);

                    break;

                case TopLevelWhileStatement whileStatement:
                    CollectDeclaredTypes(whileStatement.Statement, context, types, seen);
                    break;

                case TopLevelElseStatement elseStatement:
                    CollectDeclaredTypes(elseStatement.Statement, context, types, seen);
                    break;
            }
        }
    }

    /// <summary> Adds one declared type symbol to the cycle-detection worklist and descends into nested declarations. </summary>
    private static void CollectDeclaredType(TypeDeclaration declaration, ResolutionContext context, List<TypeSymbol> types, HashSet<TypeSymbol> seen)
    {
        if (!context.TryResolveDeclaredSymbol(declaration, out Symbol? symbol) || symbol is not TypeSymbol typeSymbol || !seen.Add(typeSymbol))
            return;

        types.Add(typeSymbol);

        if (declaration.Body is not TypeBlockBody blockBody)
            return;

        foreach (Member member in blockBody.Members)
        {
            switch (member)
            {
                case MemberTypeDeclaration typeDeclaration:
                    CollectDeclaredType(typeDeclaration.Type, context, types, seen);
                    break;

                case MemberFunctionDeclaration functionDeclaration:
                    CollectDeclaredTypes(functionDeclaration.Function, context, types, seen);
                    break;
            }
        }
    }

    /// <summary> Collects type declarations nested anywhere inside one function body. </summary>
    private static void CollectDeclaredTypes(FunctionDeclaration declaration, ResolutionContext context, List<TypeSymbol> types, HashSet<TypeSymbol> seen)
    {
        switch (declaration.Body)
        {
            case FunctionBlockBody blockBody:
                CollectDeclaredTypes(blockBody.Locals, context, types, seen);
                break;

            case FunctionLambdaBody lambdaBody:
                CollectDeclaredTypes(lambdaBody.Statement, context, types, seen);
                break;
        }
    }

    /// <summary> Collects type declarations introduced by a list of locals. </summary>
    private static void CollectDeclaredTypes(IReadOnlyList<Local> locals, ResolutionContext context, List<TypeSymbol> types, HashSet<TypeSymbol> seen)
    {
        foreach (Local local in locals)
        {
            switch (local)
            {
                case LocalTypeDeclaration typeDeclaration:
                    CollectDeclaredType(typeDeclaration.Type, context, types, seen);
                    break;

                case LocalFunctionDeclaration functionDeclaration:
                    CollectDeclaredTypes(functionDeclaration.Function, context, types, seen);
                    break;

                case LocalStatement statement:
                    CollectDeclaredTypes(statement, context, types, seen);
                    break;
            }
        }
    }

    /// <summary> Collects type declarations nested under a top-level statement subtree. </summary>
    private static void CollectDeclaredTypes(TopLevelStatement statement, ResolutionContext context, List<TypeSymbol> types, HashSet<TypeSymbol> seen)
    {
        switch (statement)
        {
            case TopLevelBlockStatement blockStatement:
                CollectDeclaredTypes(blockStatement.Locals, context, types, seen);
                break;

            case TopLevelIfStatement ifStatement:
                CollectDeclaredTypes(ifStatement.ThenStatement, context, types, seen);

                if (ifStatement.ElseStatement is not null)
                    CollectDeclaredTypes(ifStatement.ElseStatement, context, types, seen);

                break;

            case TopLevelWhileStatement whileStatement:
                CollectDeclaredTypes(whileStatement.Statement, context, types, seen);
                break;

            case TopLevelElseStatement elseStatement:
                CollectDeclaredTypes(elseStatement.Statement, context, types, seen);
                break;
        }
    }

    /// <summary> Collects type declarations nested under a local-statement subtree. </summary>
    private static void CollectDeclaredTypes(LocalStatement statement, ResolutionContext context, List<TypeSymbol> types, HashSet<TypeSymbol> seen)
    {
        switch (statement)
        {
            case LocalBlockStatement blockStatement:
                CollectDeclaredTypes(blockStatement.Locals, context, types, seen);
                break;

            case LocalIfStatement ifStatement:
                CollectDeclaredTypes(ifStatement.ThenStatement, context, types, seen);

                if (ifStatement.ElseStatement is not null)
                    CollectDeclaredTypes(ifStatement.ElseStatement, context, types, seen);

                break;

            case LocalWhileStatement whileStatement:
                CollectDeclaredTypes(whileStatement.Body, context, types, seen);
                break;

            case LocalElseStatement elseStatement:
                CollectDeclaredTypes(elseStatement.Statement, context, types, seen);
                break;
        }
    }

    /// <summary> Reports one cycle diagnostic anchored on the declared type name. </summary>
    private static void ReportCycleDiagnostic(ResolutionCoordinatorContext context, TypeSymbol type)
    {
        if (type.Declaration is not TypeDeclaration declaration)
        {
            context.Diagnostics.ReportResolutionStateError(default, $"type hierarchy cycle for '{type.Name}'");
            return;
        }

        Token nameToken = GetDeclaredNameToken(declaration.Name);
        context.Diagnostics.ReportCyclicTypeHierarchy(nameToken.Span, type.Name.ToString(), nameToken.Source);
    }

    /// <summary> Returns the token that names one declaration so diagnostics can anchor precisely. </summary>
    private static Token GetDeclaredNameToken(NamedSyntax name) => name switch
    {
        SimpleName simpleName => simpleName.Name,
        GenericName genericName => genericName.Name,
        QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetDeclaredNameToken(qualifiedName.Parts[^1]),
        _ => SyntaxSpan.GetFirstToken(name) ?? throw new InvalidOperationException($"Unhandled name syntax '{name.GetType().Name}'.")
    };

    /// <summary> Materializes a declared name for diagnostics and state-error reporting. </summary>
    private static string GetDeclaredName(NamedSyntax name) => name switch
    {
        SimpleName simpleName => simpleName.Name.Value,
        GenericName genericName => genericName.Name.Value,
        QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetDeclaredName(qualifiedName.Parts[^1]),
        _ => name.GetType().Name
    };

    /// <summary> DFS visitation state used by the project-wide cycle detector. </summary>
    private enum VisitState : byte
    {
        /// <summary> The type has not been visited yet. </summary>
        NotVisited,
        /// <summary> The type is on the active DFS stack. </summary>
        Visiting,
        /// <summary> The type and its reachable base edges have been fully processed. </summary>
        Visited
    }
}
