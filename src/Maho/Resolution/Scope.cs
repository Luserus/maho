using System.Collections.Generic;
using Maho.Symbols;

namespace Maho.Resolution;

internal sealed class Scope
{
    private Scope? parent;
    private Dictionary<string, TypeSymbol> typeSymbolTable = [];
    private Dictionary<string, IValueSymbol> valueSymbolTable = [];

    public Scope(Scope? parent = null)
    {
        this.parent = parent;
    }
}