using System.Collections.Generic;

namespace Maho.Resolution;

internal sealed class TypeParameterSymbol : Symbol
{
    public SymbolName Name { get; }
    public Symbol? GenericSymbol { get; }
    
    public List<SymbolHandle> Constraints { get; internal set; }

    public TypeParameterSymbol(SymbolID id, Scope enclosingScope, SymbolName name, Symbol? genericSymbol) : base(id, enclosingScope)
    {
        Kind = SymbolKind.TypeParameter;
        Name = name;
        GenericSymbol = genericSymbol;
        Constraints = [];
    }
}