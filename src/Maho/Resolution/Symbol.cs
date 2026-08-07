namespace Maho.Resolution;

internal abstract class Symbol
{
    public SymbolID ID { get; }
    public SymbolKind Kind { get; init; }
    public Scope EnclosingScope { get; }

    public Symbol(SymbolID id, Scope enclosingScope)
    {
        ID = id;
        EnclosingScope = enclosingScope;
    }
}
