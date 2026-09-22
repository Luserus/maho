using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class MemberAttributeSymbol : NestedAttributeSymbol
{
    public MemberAttributeSymbol(SymbolID id, SymbolPart name, Scope enclosingScope, SymbolHandle? parent, AttributeSignature? syntax) : base(id, name, enclosingScope, parent, syntax)
    {

    }
}