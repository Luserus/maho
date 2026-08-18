using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class MemberMethodSymbol : MethodSymbol
{
    public SymbolHandle? Parent { get; }

    public MemberMethodSymbol(SymbolID id, Scope enclosingScope, SymbolHandle? parent, FunctionDeclaration? syntax)
    : base(id, enclosingScope, syntax)
    {
        Parent = parent;
    }
}

