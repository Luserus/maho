namespace Maho.Resolution;

internal sealed class ParameterSymbol : Symbol
{
    public SymbolHandle? ContainingFunction { get; }

    public ParameterSymbol(SymbolID id, Scope enclosingScope, SymbolHandle? containingFunction) : base(id, enclosingScope)
    {
        Kind = SymbolKind.Parameter;
        ContainingFunction = containingFunction;
    }
}

