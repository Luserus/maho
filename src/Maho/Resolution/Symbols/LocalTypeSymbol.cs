using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class LocalTypeSymbol : NestedTypeSymbol
{
    public MethodSymbol? Parent { get; }

    public LocalTypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind, MethodSymbol? parent,
                        TypeDeclaration? syntax) : base(id, enclosingScope, name, typeKind, syntax)
    {
        Parent = parent;
    }
}