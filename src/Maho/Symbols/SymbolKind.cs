namespace Maho.Symbols;

/// <summary> Broad semantic categories used by the symbol model. </summary>
internal enum SymbolKind
{
    /// <summary> Namespace container symbol. </summary>
    Namespace,
    /// <summary> Type declaration symbol. </summary>
    Type,
    /// <summary> Function declaration symbol. </summary>
    Function,
    /// <summary> Property declaration symbol. </summary>
    Property,
    /// <summary> Function parameter symbol. </summary>
    Parameter,
    /// <summary> Generic type-parameter symbol. </summary>
    TypeParameter,
    /// <summary> Variable or field declaration symbol. </summary>
    Variable,
    /// <summary> Future label symbol category for statement-level control flow. </summary>
    Label
}
