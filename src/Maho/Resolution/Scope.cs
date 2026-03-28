using System.Collections.Generic;
using Maho.Symbols;

namespace Maho.Resolution;

internal sealed class Scope
{
    private Scope? parent;

    public Scope(Scope? parent = null)
    {
        this.parent = parent;
    }
}