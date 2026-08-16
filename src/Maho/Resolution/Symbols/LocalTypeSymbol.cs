using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class LocalTypeSymbol : NestedTypeSymbol
{
    public MethodSymbol? Parent { get; }

    public LocalTypeSymbol(SymbolID id, Scope enclosingScope, SymbolName name, TypeKind typeKind, MethodSymbol? parent,
                        IReadOnlyList<SymbolHandle> typeParameters, TypeDeclaration? syntax) : base(id, enclosingScope, name, typeKind, typeParameters, syntax)
    {
        Parent = parent;
    }
}