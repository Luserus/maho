using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class MemberSumTypeSymbol : MemberNestedTypeSymbol
{
    public MemberSumTypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind, (SymbolKind Kind, SymbolID ID)? parent, TypeDeclaration? syntax) : base(id, enclosingScope, name, typeKind, parent, syntax)
    {
    }
}