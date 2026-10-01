namespace Maho.Syntax;

/// <summary>Classifies a function as ordinary or a special member function.</summary>
public enum SpecialFunctionKind : byte
{
    /// <summary>Ordinary named function or method.</summary>
    None,
    /// <summary>Constructor: TypeName(...) { ... }</summary>
    Constructor,
    /// <summary>Destructor: ~TypeName() { ... }</summary>
    Destructor,
    /// <summary>Operator overload: return_type operator op(...) { ... }</summary>
    Operator
}
