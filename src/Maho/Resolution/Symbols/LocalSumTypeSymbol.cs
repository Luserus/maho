using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class LocalSumTypeSymbol : LocalTypeSymbol, ISumTypeSymbol
{
    public bool IsUntagged { get; set; }
    public TypeRef? DiscriminantType { get; set; }
    public List<SymbolHandle> EnumVariants { get; }
    public List<TypeRef> UnionConstituents { get; set; }
    public List<SymbolHandle> Methods { get; internal set; }
    public List<SymbolHandle> NestedTypes { get; internal set; }

    public LocalSumTypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind, SymbolHandle? parent, TypeDeclaration? syntax)
        : base(id, enclosingScope, name, typeKind, parent, syntax)
    {
        EnumVariants = [];
        UnionConstituents = [];
        Methods = [];
        NestedTypes = [];
    }
}
