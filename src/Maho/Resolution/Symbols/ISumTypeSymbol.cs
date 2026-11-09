using System.Collections.Generic;

namespace Maho.Resolution;

internal interface ISumTypeSymbol
{
    bool IsUntagged { get; set; }
    TypeRef? DiscriminantType { get; set; }
    List<SymbolHandle> EnumVariants { get; }
    List<TypeRef> UnionConstituents { get; set; }
    List<SymbolHandle> Methods { get; }
    List<SymbolHandle> NestedTypes { get; }
}

