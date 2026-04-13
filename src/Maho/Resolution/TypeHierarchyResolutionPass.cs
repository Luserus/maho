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

        public UnitResolver(ResolutionContext context) => this.context = context;

        public void Execute() => VisitTopLevels(context.Root.Members);

        private void VisitTopLevels(IReadOnlyList<TopLevel> members)
        {
            foreach (TopLevel member in members)
                VisitTopLevel(member);
        }

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

        private void VisitLocals(IReadOnlyList<Local> locals)
        {
            foreach (Local local in locals)
                VisitLocal(local);
        }

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

        private ResolvedTypeReference ResolveTypeReference(TypeSyntax syntax, Scope scope)
        {
            ResolvedTypeReference resolved = ResolveTypeReferenceCore(syntax, scope, [scope], lexicalLookup: true);
            context.ResolveTypeReference(syntax, resolved);
            return resolved;
        }

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

        private ResolvedNamedTypeReference ResolveGenericType(GenericType syntax, Scope lexicalScope, Scope[] scopes, bool lexicalLookup)
        {
            ResolvedTypeReference[] typeArguments = new ResolvedTypeReference[syntax.TypeArguments.Count];

            for (int i = 0; i < syntax.TypeArguments.Count; i++)
                typeArguments[i] = ResolveTypeReference(syntax.TypeArguments[i], lexicalScope);

            Symbol[] candidates = LookupCandidates(scopes, SymbolName.FromToken(syntax.Name), syntax.TypeArguments.Count, lexicalLookup, allowNamespaces: false, allowTypeParameters: false);
            return ResolveNamedType(syntax, syntax.Name.Value, syntax.TypeArguments.Count, typeArguments, candidates);
        }

        private ResolvedQualifiedTypeReference ResolveQualifiedType(QualifiedType syntax, Scope lexicalScope, Scope[] scopes, bool lexicalLookup)
        {
            ResolvedTypeReference left = ResolveTypeReferenceCore(syntax.Left, lexicalScope, scopes, lexicalLookup);
            Scope[] candidateScopes = CollectCandidateScopes(left);
            ResolvedTypeReference right = ResolveTypeReferenceCore(syntax.Right, lexicalScope, candidateScopes, lexicalLookup: false);
            return new ResolvedQualifiedTypeReference(syntax, left, right, [.. right.CandidateSymbols]);
        }

        private ResolvedTypeReference ResolveModifiedType(ModifiedType syntax, Scope lexicalScope, Scope[] scopes, bool lexicalLookup)
        {
            ResolvedTypeReference elementType = ResolveTypeReferenceCore(syntax.Type, lexicalScope, scopes, lexicalLookup);
            return syntax.Modifier is null
                ? elementType
                : new ResolvedModifiedTypeReference(syntax, elementType, syntax.Modifier);
        }

        private static ResolvedNamedTypeReference ResolveNamedType(TypeSyntax syntax, string name, int arity, ResolvedTypeReference[] typeArguments, Symbol[] candidates) =>
            new ResolvedNamedTypeReference(syntax, name, arity, typeArguments, candidates, CreateExplicitSignatureKey(candidates, typeArguments));

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

        private static bool IsCandidateMatch(Symbol symbol, int arity, bool allowNamespaces, bool allowTypeParameters) => symbol switch
        {
            NamespaceSymbol => allowNamespaces && arity == 0,
            TypeParameterSymbol => allowTypeParameters && arity == 0,
            TypeSymbol typeSymbol => typeSymbol.Arity == arity,
            _ => false
        };

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

        private static string JoinSignatureKeys(ReadOnlySpan<ResolvedTypeReference> typeArguments)
        {
            string[] parts = new string[typeArguments.Length];

            for (int i = 0; i < typeArguments.Length; i++)
                parts[i] = typeArguments[i].SignatureKey;

            return string.Join(",", parts);
        }

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

        private void ReportUnresolvedBaseType(ResolvedTypeReference resolvedReference)
        {
            TextSpan span = resolvedReference.Syntax.GetSpan() ?? default;
            context.Diagnostics.ReportUnresolvedTypeReference(span, resolvedReference.DisplayName, resolvedReference.Syntax.GetSource());
        }

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
        _ => SyntaxSpan.GetFirstToken(name) ?? throw new System.InvalidOperationException($"Unhandled name syntax '{name.GetType().Name}'.")
    };

    /// <summary> Materializes a declared name for diagnostics and state-error reporting. </summary>
    private static string GetDeclaredName(NamedSyntax name) => name switch
    {
        SimpleName simpleName => simpleName.Name.Value,
        GenericName genericName => genericName.Name.Value,
        QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetDeclaredName(qualifiedName.Parts[^1]),
        _ => name.GetType().Name
    };

    private enum VisitState : byte
    {
        NotVisited,
        Visiting,
        Visited
    }
}
