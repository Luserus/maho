namespace Maho.Resolution;

internal sealed class TypeParameterSymbol : Symbol
{
    public Symbol? Parent { get; }

    public TypeParameterSymbol(SymbolID id, Scope enclosingScope, Symbol? parent) : base(id, enclosingScope)
    {
        Kind = SymbolKind.TypeParameter;
        Parent = parent;
    }
}