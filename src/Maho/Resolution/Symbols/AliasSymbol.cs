namespace Maho.Resolution;

internal sealed class AliasSymbol : Symbol
{
    public AliasSymbol(SymbolID id, Scope enclosingScope) : base(id, enclosingScope)
    {
        Kind = SymbolKind.Alias;
    }
}

