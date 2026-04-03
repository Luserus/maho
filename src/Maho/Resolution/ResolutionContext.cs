using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Mutable semantic state for one compilation unit inside a coordinated project resolution run. </summary>
internal sealed class ResolutionContext
{
    private readonly Dictionary<SyntaxNode, Scope> scopes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SyntaxNode, Symbol> declaredSymbols = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeSyntax, ResolvedTypeReference> resolvedTypeReferences = new(ReferenceEqualityComparer.Instance);

    public ResolutionCoordinatorContext Project { get; }
    public CompilationUnit Root { get; }
    public DiagnosticsManager Diagnostics => Project.Diagnostics;
    public NamespaceSymbol GlobalNamespace => Project.GlobalNamespace;
    public Scope GlobalScope => Project.GlobalScope;
    public IReadOnlyList<ResolutionProjectReference> References => Project.References;

    /// <summary> Creates the unit-local semantic state for one compilation unit. </summary>
    public ResolutionContext(CompilationUnit root, ResolutionCoordinatorContext project)
    {
        Project = project;
        Root = root;

        ResolveDeclaredSymbol(root, GlobalNamespace);
        ResolveScope(root, GlobalScope);
    }

    /// <summary> Declares a symbol and associates the declaring syntax with it. </summary>
    public void DeclareSymbol(SyntaxNode syntax, Symbol symbol, Scope scope)
    {
        scope.Declare(symbol);
        ResolveDeclaredSymbol(syntax, symbol);
    }

    /// <summary> Associates a syntax node with a semantic symbol. </summary>
    public void ResolveDeclaredSymbol(SyntaxNode syntax, Symbol symbol)
    {
        if (declaredSymbols.TryGetValue(syntax, out Symbol? existing) && !ReferenceEquals(existing, symbol))
            return;

        declaredSymbols[syntax] = symbol;
    }

    /// <summary> Creates and records a nested lexical scope. </summary>
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
            ResolveScope(syntax, existing);
            return existing;
        }

        return CreateChildScope(syntax, parent, ownerSymbol);
    }

    public bool TryResolveSymbolScope(Symbol symbol, out Scope? scope) => Project.TryResolveSymbolScope(symbol, out scope);

    public bool TryResolveScope(SyntaxNode syntax, out Scope? scope) => scopes.TryGetValue(syntax, out scope);

    public bool TryResolveDeclaredSymbol(SyntaxNode syntax, out Symbol? symbol) => declaredSymbols.TryGetValue(syntax, out symbol);

    /// <summary> Associates one syntax node with the scope that semantically contains it. </summary>
    public void ResolveScope(SyntaxNode syntax, Scope scope)
    {
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

    public bool TryResolveTypeReference(TypeSyntax syntax, out ResolvedTypeReference? typeReference) => resolvedTypeReferences.TryGetValue(syntax, out typeReference);

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
