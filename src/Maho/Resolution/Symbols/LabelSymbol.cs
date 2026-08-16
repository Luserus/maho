using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class LabelSymbol : Symbol
{
    public SymbolName Name { get; }
    public SymbolHandle? ContainingFunction { get; }

    public SyntaxNode? Syntax { get; }

    public LabelSymbol(SymbolID id, Scope enclosingScope, SymbolName name, SymbolHandle? containingFunction, SyntaxNode? syntax) : base(id, enclosingScope)
    {
        Kind = SymbolKind.Label;
        Name = name;
        ContainingFunction = containingFunction;
        Syntax = syntax;
    }
}

