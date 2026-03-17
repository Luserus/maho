namespace Maho.Symbols;

/// <summary> This class represents variable symbols. </summary>
internal sealed class VariableSymbol : Symbol, IValueSymbol
{
    /// <summary> The type of the variable declared. </summary>
    public TypeSymbol Type { get; }

    /// <summary> Initializes the VariableSymbol class with type and identifier. </summary>
    /// <param name="type"> The type of the variable. </param>
    /// <param name="name"> The name of the variable. </param>
    public VariableSymbol(TypeSymbol type, string name, SymbolKind kind, Symbol parentSymbol) : base(name, kind, parentSymbol)
    {
        Type = type;
    }
}