namespace Maho.Symbols;

/// <summary> This class represents types in the language. </summary>
internal sealed class TypeSymbol : Symbol
{
    /// <summary> Initializes the TypeSymbol class. </summary>
    /// <param name="name"> The identifier of the type. </param>
    public TypeSymbol(string name, SymbolKind kind, Symbol parentSymbol) : base(name, kind, parentSymbol)
    { }
}