namespace Maho.Resolution;

internal abstract class Symbol
{
    public SymbolID ID { get; }
    public SymbolPart Name { get; }
    public SymbolKind Kind { get; init; }
    public Scope EnclosingScope { get; }

    public Symbol(SymbolID id, SymbolPart name, Scope enclosingScope)
    {
        ID = id;
        Name = name;
        EnclosingScope = enclosingScope;
    }
}
