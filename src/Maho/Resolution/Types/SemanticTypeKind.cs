namespace Maho.Resolution;

/// <summary>Represents the kind of a compiler-constructed special semantic type.</summary>
internal enum SemanticTypeKind : byte
{
    Pointer,
    Reference,
    Array,
    FixedArray,
    Span,
    Optional,
    Tuple,
    Union,
    Untagged,
    Literal
}
