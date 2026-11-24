using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class GenericParameterSymbol : Symbol
{
    public Symbol GenericSymbol { get; }
    public GenericParameterKind ParameterKind { get; }
    public bool IsVariadic { get; }
    public GenericParameterSyntax? Syntax { get; }
    public TypeRef? DefaultType { get; internal set; }

    public List<TypeRef> Constraints { get; internal set; }

    public GenericParameterSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, Symbol genericSymbol, GenericParameterKind parameterKind, bool isVariadic, GenericParameterSyntax? syntax = null) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.GenericParameter;
        GenericSymbol = genericSymbol;
        ParameterKind = parameterKind;
        IsVariadic = isVariadic;
        Syntax = syntax;
        Constraints = [];
    }
}