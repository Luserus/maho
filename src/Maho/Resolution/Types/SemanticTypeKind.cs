namespace Maho.Resolution;

/// <summary>Represents the kind of a compiler-constructed special semantic type.</summary>
internal enum SemanticTypeKind : byte
{
    Pointer,
    Reference,
    Array,
    Span,
    Optional,
    Tuple
}
