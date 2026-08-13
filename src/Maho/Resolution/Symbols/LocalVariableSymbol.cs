namespace Maho.Resolution;

internal sealed class LocalVariableSymbol : Symbol
{
    public Symbol? Parent { get; }

    public LocalVariableSymbol(SymbolID id, Scope enclosingScope, Symbol? parent) : base(id, enclosingScope)
    {
        Kind = SymbolKind.Variable;
        Parent = parent;
    }
}

