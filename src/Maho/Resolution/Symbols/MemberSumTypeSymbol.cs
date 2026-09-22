using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class MemberSumTypeSymbol : MemberTypeSymbol
{
    public MemberSumTypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind, SymbolHandle? parent, TypeDeclaration? syntax) : base(id, enclosingScope, name, typeKind, parent, syntax)
    {
    }
}