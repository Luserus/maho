using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class LocalFunctionSymbol : MethodSymbol
{
    public MethodSymbol? Parent { get; }

    public LocalFunctionSymbol(SymbolID id, Scope enclosingScope, MethodSymbol? parent, FunctionDeclaration? syntax)
    : base(id, enclosingScope, syntax)
    {
        Parent = parent;
    }
}
