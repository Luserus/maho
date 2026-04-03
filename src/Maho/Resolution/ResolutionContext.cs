using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>
/// Mutable state shared across all resolution passes for one compilation unit.
/// </summary>
internal sealed class ResolutionContext
{
    private readonly Dictionary<SyntaxNode, Scope> scopes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SyntaxNode, Symbol> declaredSymbols = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Symbol, Scope> symbolScopes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeSyntax, ResolvedTypeReference> resolvedTypeReferences = new(ReferenceEqualityComparer.Instance);

    public CompilationUnit Root { get; }
    public DiagnosticsManager Diagnostics { get; }
    public NamespaceSymbol GlobalNamespace { get; }
    public Scope GlobalScope { get; }

    /// <summary>
    /// Creates the global semantic state for the compilation unit being resolved.
    /// </summary>
    public ResolutionContext(CompilationUnit root, DiagnosticsManager diagnostics)
    {
        Root = root;
        Diagnostics = diagnostics;
        GlobalNamespace = new NamespaceSymbol(string.Empty, parentSymbol: null, root);
        GlobalScope = new Scope(parent: null, boundary: root, ownerSymbol: GlobalNamespace);

        ResolveDeclaredSymbol(root, GlobalNamespace);
        ResolveScope(root, GlobalScope);
        symbolScopes.Add(GlobalNamespace, GlobalScope);
    }

    /// <summary>
    /// Declares a symbol and associates the declaring syntax with it.
    /// </summary>
    public void DeclareSymbol(SyntaxNode syntax, Symbol symbol, Scope scope)
    {
        scope.Declare(symbol);
        ResolveDeclaredSymbol(syntax, symbol);
    }

    /// <summary>
    /// Associates a syntax node with a semantic symbol.
    /// </summary>
    public void ResolveDeclaredSymbol(SyntaxNode syntax, Symbol symbol)
    {
        if (declaredSymbols.TryGetValue(syntax, out Symbol? existing) && !ReferenceEquals(existing, symbol))
            return;

        declaredSymbols[syntax] = symbol;
    }

    /// <summary>
    /// Creates and records a nested lexical scope.
    /// </summary>
    public Scope CreateChildScope(SyntaxNode syntax, Scope parent, Symbol? ownerSymbol = null)
    {
        Scope scope = new(parent, syntax, ownerSymbol);
        ResolveScope(syntax, scope);

        if (ownerSymbol is not null)
            symbolScopes.TryAdd(ownerSymbol, scope);

        return scope;
    }

    /// <summary>
    /// Resolves the scope owned by a symbol, creating it on first use.
    /// </summary>
    public Scope ResolveSymbolScope(Symbol ownerSymbol, SyntaxNode syntax, Scope parent)
    {
        if (symbolScopes.TryGetValue(ownerSymbol, out Scope? existing))
        {
            ResolveScope(syntax, existing);
            return existing;
        }

        return CreateChildScope(syntax, parent, ownerSymbol);
    }

    public bool TryResolveSymbolScope(Symbol symbol, out Scope? scope) => symbolScopes.TryGetValue(symbol, out scope);

    public bool TryResolveScope(SyntaxNode syntax, out Scope? scope) => scopes.TryGetValue(syntax, out scope);

    public bool TryResolveDeclaredSymbol(SyntaxNode syntax, out Symbol? symbol) => declaredSymbols.TryGetValue(syntax, out symbol);

    /// <summary>
    /// Associates one syntax node with the scope that semantically contains it.
    /// </summary>
    public void ResolveScope(SyntaxNode syntax, Scope scope)
    {
        if (scopes.TryGetValue(syntax, out Scope? existing) && !ReferenceEquals(existing, scope))
            return;

        scopes[syntax] = scope;
    }

    /// <summary>
    /// Stores the semantic interpretation of declaration-site type syntax for later passes.
    /// </summary>
    public void ResolveTypeReference(TypeSyntax syntax, ResolvedTypeReference typeReference)
    {
        if (resolvedTypeReferences.TryGetValue(syntax, out ResolvedTypeReference? existing) && !ReferenceEquals(existing, typeReference))
            return;

        resolvedTypeReferences[syntax] = typeReference;
    }

    public bool TryResolveTypeReference(TypeSyntax syntax, out ResolvedTypeReference? typeReference) => resolvedTypeReferences.TryGetValue(syntax, out typeReference);

    public ResolutionResult ToResult() =>
        new(
            Root,
            GlobalNamespace,
            GlobalScope,
            scopes,
            declaredSymbols,
            symbolScopes,
            resolvedTypeReferences);
}
