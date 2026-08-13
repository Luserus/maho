namespace Maho.Resolution;

internal class NestedTypeSymbol : Symbol
{
    public Symbol? Parent { get; }

    public NestedTypeSymbol(SymbolID id, Scope enclosingScope, Symbol? parent) : base(id, enclosingScope)
    {
        Kind = SymbolKind.NestedType;
        Parent = parent;
    }
}