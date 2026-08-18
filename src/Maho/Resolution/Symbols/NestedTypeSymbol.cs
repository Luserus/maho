using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal abstract class NestedTypeSymbol : Symbol
{
    public SymbolName Name { get; }
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

    protected NestedTypeSymbol(SymbolID id, Scope enclosingScope, SymbolName name, TypeKind typeKind,
                            TypeDeclaration? syntax) : base(id, enclosingScope)
    {
        Kind = SymbolKind.NestedType;
        Name = name;
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