using System;
using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class ResolutionContext
{
    private readonly Dictionary<SyntaxNode, Scope> scopes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SyntaxNode, Symbol> declaredSymbols = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Symbol, Scope> symbolScopes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeSyntax, ResolvedTypeReference> resolvedTypeReferences = new(ReferenceEqualityComparer.Instance);

    public CompilationUnit Root { get; }
    public NamespaceSymbol GlobalNamespace { get; }
    public Scope GlobalScope { get; }

    public ResolutionContext(CompilationUnit root)
    {
        Root = root;
        GlobalNamespace = new NamespaceSymbol(string.Empty, parentSymbol: null, root);
        GlobalScope = new Scope(parent: null, boundary: root, ownerSymbol: GlobalNamespace);

        ResolveDeclaredSymbol(root, GlobalNamespace);
        ResolveScope(root, GlobalScope);
        symbolScopes.Add(GlobalNamespace, GlobalScope);
    }

    public void DeclareSymbol(SyntaxNode syntax, Symbol symbol, Scope scope)
    {
        scope.Declare(symbol);
        ResolveDeclaredSymbol(syntax, symbol);
    }

    public void ResolveDeclaredSymbol(SyntaxNode syntax, Symbol symbol)
    {
        if (declaredSymbols.TryGetValue(syntax, out Symbol? existing) && !ReferenceEquals(existing, symbol))
            throw new InvalidOperationException($"Syntax node '{syntax.GetType().Name}' is already bound to a different symbol.");

        declaredSymbols[syntax] = symbol;
    }

    public Scope CreateChildScope(SyntaxNode syntax, Scope parent, Symbol? ownerSymbol = null)
    {
        Scope scope = new(parent, syntax, ownerSymbol);
        ResolveScope(syntax, scope);

        if (ownerSymbol is not null)
            symbolScopes.Add(ownerSymbol, scope);

        return scope;
    }

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

    public void ResolveScope(SyntaxNode syntax, Scope scope)
    {
        if (scopes.TryGetValue(syntax, out Scope? existing) && !ReferenceEquals(existing, scope))
            throw new InvalidOperationException($"Syntax node '{syntax.GetType().Name}' is already bound to a different scope.");

        scopes[syntax] = scope;
    }

    public void ResolveTypeReference(TypeSyntax syntax, ResolvedTypeReference typeReference)
    {
        if (resolvedTypeReferences.TryGetValue(syntax, out ResolvedTypeReference? existing) && !ReferenceEquals(existing, typeReference))
            throw new InvalidOperationException($"Type syntax '{syntax.GetType().Name}' is already resolved to a different type reference.");

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
