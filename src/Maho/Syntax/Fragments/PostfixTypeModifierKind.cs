namespace Maho.Syntax;

/// <summary> Enumerates the supported postfix modifier shapes for types. </summary>
internal enum PostfixTypeModifierKind
{
    Slice,
    Array,
    Pointer,
    Optional,
    Reference,
    Variadic
}
