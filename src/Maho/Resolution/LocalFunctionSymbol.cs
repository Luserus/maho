namespace Maho.Resolution;

internal sealed class LocalFunctionSymbol : MethodSymbol
{
    public LocalFunctionSymbol(SymbolID id, Scope enclosingScope, Symbol? parent) : base(id, enclosingScope, parent)
    {
        Kind = SymbolKind.Method;
    }
}
