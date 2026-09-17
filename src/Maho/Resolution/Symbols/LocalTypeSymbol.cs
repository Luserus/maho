using Maho.Syntax;

namespace Maho.Resolution;

internal abstract class LocalTypeSymbol : NestedTypeSymbol
{
    public LocalTypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind, SymbolHandle? parent,
                        TypeDeclaration? syntax) : base(id, enclosingScope, name, typeKind, parent, syntax)
    {
        
    }
}