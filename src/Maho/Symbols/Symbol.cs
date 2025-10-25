namespace Maho.Symbols;

/// <summary> This class represents all the symbols in the language. </summary>
internal abstract class Symbol
{
    public string Name { get; }
    public SymbolKind Kind { get; }
    public Symbol ParentSymbol { get; }

    public Symbol(string name, SymbolKind kind, Symbol parentSymbol)
    {
        Name = name;
        Kind = kind;
        ParentSymbol = parentSymbol;
    }
}