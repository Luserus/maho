using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class LocalFunctionSymbol : MethodSymbol
{
    public LocalFunctionSymbol(SymbolID id, SymbolPart name, Scope enclosingScope, SymbolHandle? parent, FunctionDeclaration? syntax)
    : base(id, enclosingScope, name, parent, syntax)
    {
    }
}
