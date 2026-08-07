using System.Collections.Generic;

namespace Maho.Resolution;

internal sealed class Scope
{
    public Scope? Parent { get; }
    public Dictionary<SymbolHandle, Symbol> Symbols { get; }

    public List<Scope> ChildScopes { get; }

    public static Scope GlobalScope { get; } = new Scope(null);

    public Scope(Scope? parent)
    {
        Parent = parent;
        Symbols = [];
        ChildScopes = [];
    }
}