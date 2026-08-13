namespace Maho.Resolution;

internal sealed class TypeSymbol : Symbol
{
    public NamespaceTrieNode? Namespace { get; }

    public TypeSymbol(SymbolID id, Scope enclosingScope, NamespaceTrieNode? @namespace) : base(id, enclosingScope)
    {
        Kind = SymbolKind.Type;
        Namespace = @namespace;
    }
}
