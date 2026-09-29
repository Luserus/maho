using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class ParameterSymbol : Symbol
{
    public SymbolHandle? ContainingSymbol { get; }
    public List<SymbolHandle> Attributes { get; internal set; }
    public TypeRef Type { get; internal set; } = TypeRef.Unresolved;
    public Parameter? Syntax { get; }

    public ParameterSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, SymbolHandle? containingSymbol, Parameter? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Parameter;
        ContainingSymbol = containingSymbol;
        Attributes = [];
        Syntax = syntax;
    }
}