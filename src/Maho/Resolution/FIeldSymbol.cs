namespace Maho.Resolution;

internal sealed class FieldSymbol : Symbol
{
    public Symbol? Parent { get; }

    public FieldSymbol(SymbolID id, Scope enclosingScope, Symbol? parent) : base(id, enclosingScope)
    {
        Kind = SymbolKind.Field;
        Parent = parent;
    }
}

