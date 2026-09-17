using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class MemberMethodSymbol : MethodSymbol
{
    public MemberMethodSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, SymbolHandle? parent, FunctionDeclaration? syntax)
    : base(id, enclosingScope, name, parent, syntax)
    {
        
    }
}

