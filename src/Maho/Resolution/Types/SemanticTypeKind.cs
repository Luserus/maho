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
    Delegate,
    Literal,
    Generic,
    Alias,
    Conformance
}
