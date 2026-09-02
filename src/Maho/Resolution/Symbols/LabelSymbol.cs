using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class LabelSymbol : Symbol
{
    public SymbolHandle? ContainingFunction { get; }

    public SyntaxNode? Syntax { get; }

    public LabelSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, SymbolHandle? containingFunction, SyntaxNode? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Label;
        ContainingFunction = containingFunction;
        Syntax = syntax;
    }
}

