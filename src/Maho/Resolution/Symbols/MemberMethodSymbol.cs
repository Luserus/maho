namespace Maho.Resolution;

internal sealed class MemberMethodSymbol : MethodSymbol
{
    public MemberMethodSymbol(SymbolID id, Scope enclosingScope, Symbol? parent) : base(id, enclosingScope, parent)
    {
        Kind = SymbolKind.Method;
    }
}

