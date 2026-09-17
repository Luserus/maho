using Maho.Syntax;

namespace Maho.Resolution;

internal abstract class MemberTypeSymbol : NestedTypeSymbol
{
    public MemberTypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind, SymbolHandle? parent,
                                TypeDeclaration? syntax) : base(id, enclosingScope, name, typeKind, parent, syntax)
    {
        
    }
}