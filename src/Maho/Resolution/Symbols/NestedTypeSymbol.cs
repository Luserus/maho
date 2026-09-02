using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal abstract class NestedTypeSymbol : Symbol
{
    public TypeKind TypeKind { get; }
    public TypeFlags Flags { get; internal set; }
    

    public IReadOnlyList<SymbolHandle> TypeParameters { get; internal set; }
    public List<SymbolHandle> BaseTypes { get; internal set; }
    public List<SymbolHandle> Attributes { get; internal set; }

    public List<SymbolHandle> Fields { get; internal set; }
    public List<SymbolHandle> Properties { get; internal set; }
    public List<SymbolHandle> Methods { get; internal set; }
    public List<SymbolHandle> NestedTypes { get; internal set; }

    public TypeDeclaration? Syntax { get; }

    protected NestedTypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind,
                            TypeDeclaration? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.NestedType;
        TypeKind = typeKind;
        TypeParameters = [];
        BaseTypes = [];
        Attributes = [];
        Fields = [];
        Properties = [];
        Methods = [];
        NestedTypes = [];
        Syntax = syntax;
    }
}