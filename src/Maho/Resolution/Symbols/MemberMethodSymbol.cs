using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class MemberMethodSymbol : MethodSymbol
{
    public SymbolHandle? Parent { get; }

    public MemberMethodSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, SymbolHandle? parent, FunctionDeclaration? syntax)
    : base(id, enclosingScope, name, syntax)
    {
        Parent = parent;
    }
}

