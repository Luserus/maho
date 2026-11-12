namespace Maho.Syntax;

/// <summary>Classifies a function as ordinary or a special member function.</summary>
public enum SpecialFunctionKind : byte
{
    /// <summary>Ordinary named function or method.</summary>
    None,
    /// <summary>Literal function: literal suffix(kind param) ...</summary>
    Literal
}
