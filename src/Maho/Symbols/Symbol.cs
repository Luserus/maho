namespace Maho.Symbols;

/// <summary> This class represents all the symbols in the language. </summary>
internal abstract class Symbol
{
    public SymbolKind Kind { get; }
    public Symbol ParentSymbol { get; }

    public Symbol(SymbolKind kind, Symbol parentSymbol)
    {
        Kind = kind;
        ParentSymbol = parentSymbol;
    }
}