using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class ProductTypeSymbol : TypeSymbol
{
    public List<SymbolHandle> Fields { get; internal set; }
    public List<SymbolHandle> Properties { get; internal set; }
    public List<SymbolHandle> Methods { get; internal set; }
    public List<SymbolHandle> NestedTypes { get; internal set; }
    /// <summary>Fast lookup: operator kind -> list of overloading method handles.</summary>
    public Dictionary<OperatorKind, List<SymbolHandle>> OperatorOverloads { get; internal set; }

    public ProductTypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind, NamespaceTrieNode? containingNamespace, TypeDeclaration? syntax)
    : base(id, enclosingScope, name, typeKind, containingNamespace, syntax)
    {
        Fields = [];
        Properties = [];
        Methods = [];
        NestedTypes = [];
        OperatorOverloads = [];
    }
}