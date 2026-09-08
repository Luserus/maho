using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class LocalSumTypeSymbol : LocalTypeSymbol
{
    public LocalSumTypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind, MethodSymbol? parent, TypeDeclaration? syntax) : base(id, enclosingScope, name, typeKind, parent, syntax)
    {
    }
}