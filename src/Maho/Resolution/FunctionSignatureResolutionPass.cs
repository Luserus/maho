using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>
/// Resolves function return types and parameter types after declaration discovery and type-hierarchy
/// binding have produced the canonical symbol/scope graph. The pass stays unit-local and parallel so
/// later duplicate-function analysis can rely on fully populated signature data.
/// </summary>
internal sealed class FunctionSignatureResolutionPass : ResolutionPass
{
    /// <summary>
    /// Function signature binding only reads shared project state and writes to unit-local maps plus
    /// each declaration's own function/parameter symbols.
    /// </summary>
    public override void Execute(ResolutionCoordinatorContext context)
    {
        Parallel.For(0, context.Units.Length, unitIndex => ResolveUnit(context.Units[unitIndex]));
    }

    /// <summary> Resolves every function signature declared inside one compilation unit. </summary>
    private void ResolveUnit(ResolutionContext context) => VisitTopLevels(context, context.Root.Members);

    /// <summary> Visits every top-level item in source order. </summary>
    private void VisitTopLevels(ResolutionContext context, IReadOnlyList<TopLevel> members)
    {
        foreach (TopLevel member in members)
            VisitTopLevel(context, member);
    }

    /// <summary> Dispatches one top-level item to the traversal needed for signature resolution. </summary>
    private void VisitTopLevel(ResolutionContext context, TopLevel member)
    {
        switch (member)
        {
            case NamespaceDeclaration namespaceDeclaration when namespaceDeclaration.Body is NamespaceBlockBody blockBody:
                VisitTopLevels(context, blockBody.Members);
                break;

            case TopLevelTypeDeclaration typeDeclaration:
                VisitTypeDeclaration(context, typeDeclaration.Type);
                break;

            case TopLevelFunctionDeclaration functionDeclaration:
                VisitFunctionDeclaration(context, functionDeclaration.Function);
                break;

            case TopLevelBlockStatement blockStatement:
                VisitLocals(context, blockStatement.Locals);
                break;

            case TopLevelIfStatement ifStatement:
                VisitTopLevelStatement(context, ifStatement.ThenStatement);

                if (ifStatement.ElseStatement is not null)
                    VisitTopLevelStatement(context, ifStatement.ElseStatement);

                break;

            case TopLevelWhileStatement whileStatement:
                VisitTopLevelStatement(context, whileStatement.Statement);
                break;

            case TopLevelElseStatement elseStatement:
                VisitTopLevelStatement(context, elseStatement.Statement);
                break;
        }
    }

    /// <summary> Traverses nested top-level statements so local functions are not skipped. </summary>
    private void VisitTopLevelStatement(ResolutionContext context, TopLevelStatement statement)
    {
        switch (statement)
        {
            case TopLevelBlockStatement blockStatement:
                VisitLocals(context, blockStatement.Locals);
                break;

            case TopLevelIfStatement ifStatement:
                VisitTopLevelStatement(context, ifStatement.ThenStatement);

                if (ifStatement.ElseStatement is not null)
                    VisitTopLevelStatement(context, ifStatement.ElseStatement);

                break;

            case TopLevelWhileStatement whileStatement:
                VisitTopLevelStatement(context, whileStatement.Statement);
                break;

            case TopLevelElseStatement elseStatement:
                VisitTopLevelStatement(context, elseStatement.Statement);
                break;
        }
    }

    /// <summary> Traverses one type declaration and any nested declarations that can contain functions. </summary>
    private void VisitTypeDeclaration(ResolutionContext context, TypeDeclaration declaration)
    {
        if (declaration.Body is not TypeBlockBody blockBody)
            return;

        foreach (Member member in blockBody.Members)
            VisitMember(context, member);
    }

    /// <summary> Traverses one member declaration and resolves signatures for nested functions. </summary>
    private void VisitMember(ResolutionContext context, Member member)
    {
        switch (member)
        {
            case MemberTypeDeclaration typeDeclaration:
                VisitTypeDeclaration(context, typeDeclaration.Type);
                break;

            case MemberFunctionDeclaration functionDeclaration:
                VisitFunctionDeclaration(context, functionDeclaration.Function);
                break;
        }
    }

    /// <summary> Resolves one function signature, then traverses its body for nested declarations. </summary>
    private void VisitFunctionDeclaration(ResolutionContext context, FunctionDeclaration declaration)
    {
        ResolveSignature(context, declaration);

        switch (declaration.Body)
        {
            case FunctionBlockBody blockBody:
                VisitLocals(context, blockBody.Locals);
                break;

            case FunctionLambdaBody lambdaBody:
                VisitLocalStatement(context, lambdaBody.Statement);
                break;
        }
    }

    /// <summary> Visits all local items inside one lexical container. </summary>
    private void VisitLocals(ResolutionContext context, IReadOnlyList<Local> locals)
    {
        foreach (Local local in locals)
            VisitLocal(context, local);
    }

    /// <summary> Dispatches one local declaration or statement to the traversal needed for signature resolution. </summary>
    private void VisitLocal(ResolutionContext context, Local local)
    {
        switch (local)
        {
            case LocalTypeDeclaration typeDeclaration:
                VisitTypeDeclaration(context, typeDeclaration.Type);
                break;

            case LocalFunctionDeclaration functionDeclaration:
                VisitFunctionDeclaration(context, functionDeclaration.Function);
                break;

            case LocalStatement statement:
                VisitLocalStatement(context, statement);
                break;
        }
    }

    /// <summary> Traverses nested local statements so embedded local functions are not skipped. </summary>
    private void VisitLocalStatement(ResolutionContext context, LocalStatement statement)
    {
        switch (statement)
        {
            case LocalBlockStatement blockStatement:
                VisitLocals(context, blockStatement.Locals);
                break;

            case LocalIfStatement ifStatement:
                VisitLocalStatement(context, ifStatement.ThenStatement);

                if (ifStatement.ElseStatement is not null)
                    VisitLocalStatement(context, ifStatement.ElseStatement);

                break;

            case LocalWhileStatement whileStatement:
                VisitLocalStatement(context, whileStatement.Body);
                break;

            case LocalElseStatement elseStatement:
                VisitLocalStatement(context, elseStatement.Statement);
                break;
        }
    }

    /// <summary> Resolves and stores the parameter and return types for one function declaration. </summary>
    private void ResolveSignature(ResolutionContext context, FunctionDeclaration declaration)
    {
        if (!context.TryResolveDeclaredSymbol(declaration, out Symbol? declaredSymbol) || declaredSymbol is not FunctionSymbol functionSymbol)
        {
            context.Diagnostics.ReportResolutionStateError(
                declaration.GetSpan() ?? default,
                $"function declaration '{GetDeclaredName(declaration.Signature.Identifier)}'",
                declaration.GetSource());
            return;
        }

        if (!context.TryResolveSymbolScope(functionSymbol, out Scope? functionScope) || functionScope is null)
        {
            context.Diagnostics.ReportResolutionStateError(
                declaration.GetSpan() ?? default,
                $"function scope '{functionSymbol.Name}'",
                declaration.GetSource());
            return;
        }

        ResolveParameters(context, declaration.Signature.Parameters, functionSymbol.Parameters, functionScope);
        var returnType = ResolveTypeReference(context, declaration.Signature.ReturnType, functionScope, SignatureTypeContext.ReturnType);
        functionSymbol.ResolveSignature(returnType);
    }

    /// <summary> Resolves and stores every parameter type declared by one function signature. </summary>
    private void ResolveParameters(ResolutionContext context, SeparatedSyntaxList<Parameter> parameters, ReadOnlySpan<ParameterSymbol> parameterSymbols, Scope functionScope)
    {
        int count = Math.Min(parameters.Count, parameterSymbols.Length);

        for (int i = 0; i < count; i++)
        {
            Parameter parameter = parameters[i];
            ResolvedTypeReference parameterType = ResolveTypeReference(context, parameter.Declarator.Type, functionScope, SignatureTypeContext.ParameterType);
            parameterSymbols[i].ResolveType(parameterType);
        }
    }

    /// <summary> Resolves one signature type-syntax occurrence and records the semantic result in the unit map. </summary>
    private ResolvedTypeReference ResolveTypeReference(ResolutionContext context, TypeSyntax syntax, Scope scope, SignatureTypeContext typeContext)
    {
        ResolvedTypeReference resolved = ResolveTypeReferenceCore(context, syntax, scope, [scope], lexicalLookup: true, typeContext);
        context.ResolveTypeReference(syntax, resolved);
        ReportLookupDiagnosticsIfNeeded(context, resolved);
        return resolved;
    }

    /// <summary> Resolves any supported type-syntax form under the provided lookup rules and signature context. </summary>
    private ResolvedTypeReference ResolveTypeReferenceCore(ResolutionContext context, TypeSyntax syntax, Scope lexicalScope, Scope[] scopes, bool lexicalLookup, SignatureTypeContext typeContext)
    {
        return syntax switch
        {
            SimpleType simpleType => ResolveSimpleType(simpleType, scopes, lexicalLookup, typeContext),
            GenericType genericType => ResolveGenericType(context, genericType, lexicalScope, scopes, lexicalLookup),
            QualifiedType qualifiedType => ResolveQualifiedType(context, qualifiedType, lexicalScope, scopes, lexicalLookup, typeContext),
            ModifiedType modifiedType => ResolveModifiedType(context, modifiedType, lexicalScope, scopes, lexicalLookup, typeContext),
            _ => throw new InvalidOperationException($"Unhandled type syntax '{syntax.GetType().Name}'.")
        };
    }

    /// <summary>
    /// Resolves a simple unqualified type name, only treating the explicitly supported signature
    /// forms `dyn` and return-position `var` specially. All other names go through normal scope lookup.
    /// </summary>
    private static ResolvedTypeReference ResolveSimpleType(SimpleType syntax, Scope[] scopes, bool lexicalLookup, SignatureTypeContext typeContext)
    {
        if (lexicalLookup && typeContext is SignatureTypeContext.ReturnType && syntax.Name.MatchingKind is MatchingKeywordKind.Var)
            return new ResolvedKeywordTypeReference(syntax, syntax.Name.Value);

        if (lexicalLookup && syntax.Name.MatchingKind is MatchingKeywordKind.Dyn)
            return new ResolvedKeywordTypeReference(syntax, syntax.Name.Value);

        Symbol[] candidates = LookupCandidates(scopes, SymbolName.FromToken(syntax.Name), 0, lexicalLookup, allowNamespaces: true, allowTypeParameters: true);
        return ResolveNamedType(syntax, syntax.Name.Value, 0, [], candidates);
    }

    /// <summary> Resolves a generic type reference, including all type arguments, under the current scope. </summary>
    private ResolvedNamedTypeReference ResolveGenericType(ResolutionContext context, GenericType syntax, Scope lexicalScope, Scope[] scopes, bool lexicalLookup)
    {
        ResolvedTypeReference[] typeArguments = new ResolvedTypeReference[syntax.TypeArguments.Count];

        for (int i = 0; i < syntax.TypeArguments.Count; i++)
            typeArguments[i] = ResolveTypeReference(context, syntax.TypeArguments[i], lexicalScope, SignatureTypeContext.ParameterType);

        Symbol[] candidates = LookupCandidates(scopes, SymbolName.FromToken(syntax.Name), syntax.TypeArguments.Count, lexicalLookup, allowNamespaces: false, allowTypeParameters: false);
        return ResolveNamedType(syntax, syntax.Name.Value, syntax.TypeArguments.Count, typeArguments, candidates);
    }

    /// <summary> Resolves a qualified type reference by treating the left side as a container for the right. </summary>
    private ResolvedQualifiedTypeReference ResolveQualifiedType(ResolutionContext context, QualifiedType syntax, Scope lexicalScope, Scope[] scopes, bool lexicalLookup, SignatureTypeContext typeContext)
    {
        ResolvedTypeReference left = ResolveTypeReferenceCore(context, syntax.Left, lexicalScope, scopes, lexicalLookup, typeContext);
        Scope[] candidateScopes = CollectCandidateScopes(context, left);
        ResolvedTypeReference right = ResolveTypeReferenceCore(context, syntax.Right, lexicalScope, candidateScopes, lexicalLookup: false, typeContext);
        return new ResolvedQualifiedTypeReference(syntax, left, right, [.. right.CandidateSymbols]);
    }

    /// <summary> Resolves the underlying element type, then reapplies any postfix type modifier. </summary>
    private ResolvedTypeReference ResolveModifiedType(ResolutionContext context, ModifiedType syntax, Scope lexicalScope, Scope[] scopes, bool lexicalLookup, SignatureTypeContext typeContext)
    {
        ResolvedTypeReference elementType = ResolveTypeReferenceCore(context, syntax.Type, lexicalScope, scopes, lexicalLookup, typeContext);
        ResolvedTypeReference resolved = syntax.Modifier is null
            ? elementType
            : new ResolvedModifiedTypeReference(syntax, elementType, syntax.Modifier);

        if (!ReferenceEquals(resolved.Syntax, syntax))
            context.ResolveTypeReference(syntax, resolved);

        return resolved;
    }

    /// <summary> Creates the canonical semantic object for one simple or generic named type reference. </summary>
    private static ResolvedNamedTypeReference ResolveNamedType(TypeSyntax syntax, string name, int arity, ResolvedTypeReference[] typeArguments, Symbol[] candidates) =>
        new(syntax, name, arity, typeArguments, candidates, CreateExplicitSignatureKey(candidates, typeArguments));

    /// <summary> Collects the member scopes owned by the left side of a qualified type reference. </summary>
    private static Scope[] CollectCandidateScopes(ResolutionContext context, ResolvedTypeReference left)
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

    /// <summary> Reports unresolved or ambiguous lookup diagnostics for one reference unless a keyword form handled it already. </summary>
    private static void ReportLookupDiagnosticsIfNeeded(ResolutionContext context, ResolvedTypeReference resolvedReference)
    {
        if (resolvedReference is ResolvedKeywordTypeReference)
            return;

        int typeCandidateCount = 0;

        foreach (Symbol candidate in resolvedReference.CandidateSymbols)
        {
            if (candidate is not TypeSymbol and not TypeParameterSymbol)
                continue;

            typeCandidateCount++;

            if (typeCandidateCount > 1)
            {
                context.Diagnostics.ReportAmbiguousTypeReference(
                    resolvedReference.Syntax.GetSpan() ?? default,
                    resolvedReference.DisplayName,
                    resolvedReference.Syntax.GetSource());
                return;
            }
        }

        if (typeCandidateCount == 0)
        {
            context.Diagnostics.ReportUnresolvedTypeReference(
                resolvedReference.Syntax.GetSpan() ?? default,
                resolvedReference.DisplayName,
                resolvedReference.Syntax.GetSource());
        }
    }

    /// <summary> Materializes a declared name for diagnostics and state-error reporting. </summary>
    private static string GetDeclaredName(NamedSyntax name) => name switch
    {
        SimpleName simpleName => simpleName.Name.Value,
        GenericName genericName => genericName.Name.Value,
        QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetDeclaredName(qualifiedName.Parts[^1]),
        _ => name.GetType().Name
    };

    /// <summary> Distinguishes the signature position currently being resolved. </summary>
    private enum SignatureTypeContext : byte
    {
        /// <summary> The type syntax is the function return type. </summary>
        ReturnType,
        /// <summary> The type syntax is one function parameter type. </summary>
        ParameterType
    }
}
