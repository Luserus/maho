using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class LocalFunctionSymbol : MethodSymbol
{
    public MethodSymbol? Parent { get; }

    public LocalFunctionSymbol(SymbolID id, SymbolPart name, Scope enclosingScope, MethodSymbol? parent, FunctionDeclaration? syntax)
    : base(id, enclosingScope, name, syntax)
    {
        Parent = parent;
    }
}
