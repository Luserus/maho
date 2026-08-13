namespace Maho.Resolution;

internal abstract class MethodSymbol : Symbol
{
    public Symbol? Parent { get; }
    
    protected MethodSymbol(SymbolID id, Scope enclosingScope, Symbol? parent) : base(id, enclosingScope)
    {
        Parent = parent;
    }
}

