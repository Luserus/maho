namespace Maho.Resolution;

/// <summary>Represents the kind of a compiler-constructed special semantic type.</summary>
internal enum SemanticTypeKind : byte
{
    Pointer,
    Reference,
    Slice,
    Array,
    Optional,
    Tuple,
    Union,
    Untagged,
    Dyn,
    Function,
    Literal,
    Generic,
    Alias,
    Conformance,
    DerivedSelf
}
