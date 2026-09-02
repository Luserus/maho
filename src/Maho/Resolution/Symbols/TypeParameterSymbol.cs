using System.Collections.Generic;

namespace Maho.Resolution;

internal sealed class TypeParameterSymbol : Symbol
{
    public Symbol GenericSymbol { get; }
    
    public List<SymbolHandle> Constraints { get; internal set; }

    public TypeParameterSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, Symbol genericSymbol) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.TypeParameter;
        GenericSymbol = genericSymbol;
        Constraints = [];
    }
}