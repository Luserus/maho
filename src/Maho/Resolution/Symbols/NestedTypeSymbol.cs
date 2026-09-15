using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal abstract class NestedTypeSymbol : Symbol
{
    public TypeKind TypeKind { get; }
    public TypeFlags Flags { get; internal set; }
    

    public IReadOnlyList<SymbolHandle> GenericParameters { get; internal set; }
    public List<SymbolHandle> BaseTypes { get; internal set; }
    public List<SymbolHandle> Attributes { get; internal set; }

    

    public TypeDeclaration? Syntax { get; }

    protected NestedTypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind,
                            TypeDeclaration? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.NestedType;
        TypeKind = typeKind;
        GenericParameters = [];
        BaseTypes = [];
        Attributes = [];
        Syntax = syntax;
    }
}
