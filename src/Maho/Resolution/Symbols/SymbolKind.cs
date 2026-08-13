namespace Maho.Resolution;

/// <summary> Broad semantic categories used by the symbol model. </summary>
internal enum SymbolKind
{
    Alias,
    /// <summary> Namespace container symbol. </summary>
    Namespace,
    /// <summary> Type declaration symbol. </summary>
    Type,
    /// <summary> Function declaration symbol. </summary>
    NestedType,
    Function,
    Method,
    /// <summary> Property declaration symbol. </summary>
    Property,
    /// <summary> Function parameter symbol. </summary>
    Parameter,
    /// <summary> Generic type-parameter symbol. </summary>
    TypeParameter,
    /// <summary> Variable or field declaration symbol. </summary>
    Variable,
    /// <summary> Future label symbol category for statement-level control flow. </summary>
    Field,
    GlobalVariable,
    Label
}

