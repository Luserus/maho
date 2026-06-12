namespace Maho.Resolution;

internal abstract class Symbol
{
    public SymbolID ID { get; }
    public SymbolKind Kind { get; }
    public Scope EnclosingScope { get; }
    public Symbol? Parent { get; }


    public Symbol(SymbolID id, SymbolKind kind, Scope enclosingScope, Symbol? parent)
    {
        ID = id;
        Kind = kind;
        EnclosingScope = enclosingScope;
        Parent = parent;
    }
}
