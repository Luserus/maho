using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class MemberNestedTypeSymbol : NestedTypeSymbol
{
    public SymbolHandle? Parent { get; }

    public MemberNestedTypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind, SymbolHandle? parent,
                                TypeDeclaration? syntax) : base(id, enclosingScope, name, typeKind, syntax)
    {
        Parent = parent;
    }
}