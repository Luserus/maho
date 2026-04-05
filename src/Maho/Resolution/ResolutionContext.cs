using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Mutable semantic state for one compilation unit inside a coordinated project resolution run. </summary>
internal sealed class ResolutionContext
{
    /// <summary>
    /// Maps syntax nodes to the lexical scope semantically attached to them. This covers both
    /// symbol-owned scopes such as functions/types and purely lexical scopes such as blocks.
    /// </summary>
    private readonly Dictionary<SyntaxNode, Scope> scopes = new(ReferenceEqualityComparer.Instance);
    /// <summary>
    /// Maps declaration syntax to the symbol introduced by that syntax. This lets later passes jump
    /// from parser nodes to semantic identities without rediscovering declarations.
    /// </summary>
    private readonly Dictionary<SyntaxNode, Symbol> declaredSymbols = new(ReferenceEqualityComparer.Instance);
    /// <summary>
    /// Stores declaration-site type-reference models built by later passes. Pass 1 intentionally
    /// leaves this empty so type work can be grouped in a later semantic stage.
    /// </summary>
    private readonly Dictionary<TypeSyntax, ResolvedTypeReference> resolvedTypeReferences = new(ReferenceEqualityComparer.Instance);

    /// <summary> Shared project-wide semantic state for this unit. </summary>
    public ResolutionCoordinatorContext Project { get; }
    /// <summary> Root compilation unit whose local semantic maps are stored here. </summary>
    public CompilationUnit Root { get; }
    /// <summary> Convenience projection of the shared diagnostics sink. </summary>
    public DiagnosticsManager Diagnostics => Project.Diagnostics;
    /// <summary> Convenience projection of the project root namespace. </summary>
    public NamespaceSymbol GlobalNamespace => Project.GlobalNamespace;
    /// <summary> Convenience projection of the project global lexical scope. </summary>
    public Scope GlobalScope => Project.GlobalScope;
    /// <summary> Convenience projection of referenced project semantic surfaces. </summary>
    public IReadOnlyList<ResolutionProjectReference> References => Project.References;

    /// <summary> Creates the unit-local semantic state for one compilation unit. </summary>
    public ResolutionContext(CompilationUnit root, ResolutionCoordinatorContext project)
    {
        Project = project;
        Root = root;

        // Every compilation unit starts life inside the shared global namespace/scope, so unit-local
        // lookups can always fall back to that root without special cases.
        ResolveDeclaredSymbol(root, GlobalNamespace);
        ResolveScope(root, GlobalScope);
    }

    /// <summary> Declares a symbol and associates the declaring syntax with it. </summary>
    public void DeclareSymbol(SyntaxNode syntax, Symbol symbol, Scope scope)
    {
        // Declaration storage is split in two directions:
        //   1. put the symbol into the scope's name table
        //   2. remember which syntax node declared that symbol
        scope.Declare(symbol);
        ResolveDeclaredSymbol(syntax, symbol);
    }

    /// <summary> Associates a syntax node with a semantic symbol. </summary>
    public void ResolveDeclaredSymbol(SyntaxNode syntax, Symbol symbol)
    {
        // Multiple wrappers may legitimately point at the same symbol, but one syntax node should
        // never silently change owners once a pass has associated it.
        if (declaredSymbols.TryGetValue(syntax, out Symbol? existing) && !ReferenceEquals(existing, symbol))
            return;

        declaredSymbols[syntax] = symbol;
    }

    /// <summary>
    /// Creates and records a nested lexical scope. If an owner symbol is provided, the new scope is
    /// also registered in the project-wide symbol -> scope map.
    /// </summary>
    public Scope CreateChildScope(SyntaxNode syntax, Scope parent, Symbol? ownerSymbol = null)
    {
        Scope scope = new(parent, syntax, ownerSymbol);
        ResolveScope(syntax, scope);

        if (ownerSymbol is not null)
            Project.ResolveSymbolScope(ownerSymbol, scope);

        return scope;
    }

    /// <summary> Resolves the scope owned by a symbol, creating it on first use. </summary>
    public Scope ResolveSymbolScope(Symbol ownerSymbol, SyntaxNode syntax, Scope parent)
    {
        if (Project.TryResolveSymbolScope(ownerSymbol, out Scope? existing) && existing is not null)
        {
            // Different syntax nodes can legitimately point at the same owned scope, such as a
            // wrapper declaration node and its inner declaration node.
            ResolveScope(syntax, existing);
            return existing;
        }

        return CreateChildScope(syntax, parent, ownerSymbol);
    }

    /// <summary> Attempts to resolve the scope owned by a symbol from the shared project map. </summary>
    public bool TryResolveSymbolScope(Symbol symbol, out Scope? scope) => Project.TryResolveSymbolScope(symbol, out scope);

    /// <summary> Attempts to resolve the lexical scope attached to a syntax node in this unit. </summary>
    public bool TryResolveScope(SyntaxNode syntax, out Scope? scope) => scopes.TryGetValue(syntax, out scope);

    /// <summary> Attempts to resolve the symbol declared by a syntax node in this unit. </summary>
    public bool TryResolveDeclaredSymbol(SyntaxNode syntax, out Symbol? symbol) => declaredSymbols.TryGetValue(syntax, out symbol);

    /// <summary> Associates one syntax node with the scope that semantically contains it. </summary>
    public void ResolveScope(SyntaxNode syntax, Scope scope)
    {
        // As with declared symbols, a syntax node should not bounce between different scopes once
        // established. If that happens, some pass is disagreeing about structural ownership.
        if (scopes.TryGetValue(syntax, out Scope? existing) && !ReferenceEquals(existing, scope))
            return;

        scopes[syntax] = scope;
    }

    /// <summary> Stores the semantic interpretation of declaration-site type syntax for later passes. </summary>
    public void ResolveTypeReference(TypeSyntax syntax, ResolvedTypeReference typeReference)
    {
        if (resolvedTypeReferences.TryGetValue(syntax, out ResolvedTypeReference? existing) && !ReferenceEquals(existing, typeReference))
            return;

        resolvedTypeReferences[syntax] = typeReference;
    }

    /// <summary> Attempts to resolve a previously stored semantic type-reference model. </summary>
    public bool TryResolveTypeReference(TypeSyntax syntax, out ResolvedTypeReference? typeReference) => resolvedTypeReferences.TryGetValue(syntax, out typeReference);

    /// <summary> Freezes the unit-local semantic maps into a stable result object. </summary>
    public ResolutionResult ToResult() =>
        new(
            Root,
            GlobalNamespace,
            GlobalScope,
            scopes,
            declaredSymbols,
            Project.SymbolScopes,
            resolvedTypeReferences);
}
