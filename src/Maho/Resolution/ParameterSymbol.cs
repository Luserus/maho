namespace Maho.Resolution;

internal sealed class ParameterSymbol : Symbol
{
    public Symbol? Parent { get; }

    public ParameterSymbol(SymbolID id, Scope enclosingScope, Symbol? parent) : base(id, enclosingScope)
    {
        Kind = SymbolKind.Parameter;
        Parent = parent;
    }
}

