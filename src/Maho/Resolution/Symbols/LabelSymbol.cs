namespace Maho.Resolution;

internal sealed class LabelSymbol : Symbol
{
    public Symbol? Parent { get; }

    public LabelSymbol(SymbolID id, Scope enclosingScope, Symbol? parent) : base(id, enclosingScope)
    {
        Kind = SymbolKind.Label;
        Parent = parent;
    }
}

