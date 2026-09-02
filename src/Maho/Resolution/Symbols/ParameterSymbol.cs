namespace Maho.Resolution;

internal sealed class ParameterSymbol : Symbol
{
    public SymbolHandle? ContainingFunction { get; }

    public ParameterSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, SymbolHandle? containingFunction) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Parameter;
        ContainingFunction = containingFunction;
    }
}

