namespace Maho.Resolution;

internal sealed class GlobalVariableSymbol : Symbol
{
    public NamespaceTrieNode? Namespace;

    public GlobalVariableSymbol(SymbolID id, Scope enclosingScope, NamespaceTrieNode? @namespace) : base(id, enclosingScope)
    {
        Kind = SymbolKind.GlobalVariable;
        Namespace = @namespace;
    }
}

