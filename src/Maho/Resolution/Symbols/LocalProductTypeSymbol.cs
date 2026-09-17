using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class LocalProductTypeSymbol : LocalTypeSymbol
{
    public List<SymbolHandle> Fields { get; internal set; }
    public List<SymbolHandle> Properties { get; internal set; }
    public List<SymbolHandle> Methods { get; internal set; }
    public List<SymbolHandle> NestedTypes { get; internal set; }

    public LocalProductTypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind, SymbolHandle? parent, TypeDeclaration? syntax) : base(id, enclosingScope, name, typeKind, parent, syntax)
    {
        Fields = [];
        Properties = [];
        Methods = [];
        NestedTypes = [];
    }
}
