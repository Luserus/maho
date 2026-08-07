namespace Maho.Resolution;

internal sealed class FunctionSymbol : Symbol
{
    public NamespaceTrieNode? Namespace { get; }

    public FunctionSymbol(SymbolID id, Scope enclosingScope, NamespaceTrieNode? @namespace) : base(id, enclosingScope)
    {
        Kind = SymbolKind.Function;
        Namespace = @namespace;
    }
}