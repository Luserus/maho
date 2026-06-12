namespace Maho.Resolution;

internal sealed class NamespaceSymbol : Symbol
{
    public static NamespaceSymbol Global { get; } = new NamespaceSymbol(0, SymbolKind.Namespace, Scope.GlobalScope, null);

    public NamespaceSymbol(SymbolID id, SymbolKind kind, Scope enclosingScope, Symbol? parent) : base(id, kind, enclosingScope, parent)
    {

    }
}

