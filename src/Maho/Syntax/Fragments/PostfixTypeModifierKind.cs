namespace Maho.Syntax;

/// <summary> Enumerates the supported postfix modifier shapes for types. </summary>
internal enum PostfixTypeModifierKind
{
    Array,
    FixedArray,
    Pointer,
    Optional,
    Reference
}
