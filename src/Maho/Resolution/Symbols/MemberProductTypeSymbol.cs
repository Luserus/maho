using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class MemberProductTypeSymbol : MemberTypeSymbol
{
    public List<SymbolHandle> Fields { get; internal set; }
    public List<SymbolHandle> Properties { get; internal set; }
    public List<SymbolHandle> Methods { get; internal set; }
    public List<SymbolHandle> NestedTypes { get; internal set; }
    /// <summary>Fast lookup: operator kind -> list of overloading method handles.</summary>
    public Dictionary<OperatorKind, List<SymbolHandle>> OperatorOverloads { get; internal set; }

    public MemberProductTypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind, SymbolHandle? parent, TypeDeclaration? syntax) : base(id, enclosingScope, name, typeKind, parent, syntax)
    {
        Fields = [];
        Properties = [];
        Methods = [];
        NestedTypes = [];
        OperatorOverloads = [];
    }
}