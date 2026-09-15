using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class TypeParameterSymbol : Symbol
{
    public Symbol GenericSymbol { get; }
    public GenericParameterKind ParameterKind { get; }
    public bool IsVariadic { get; }
    
    public List<SymbolHandle> Constraints { get; internal set; }

    public TypeParameterSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, Symbol genericSymbol, GenericParameterKind parameterKind, bool isVariadic) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.TypeParameter;
        GenericSymbol = genericSymbol;
        ParameterKind = parameterKind;
        IsVariadic = isVariadic;
        Constraints = [];
    }
}
