using System;

namespace Maho.Resolution;

/// <summary>Represents an optional type (<c>T?</c>).</summary>
internal sealed class OptionalSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Optional;
    public TypeRef ElementType { get; }

    public override bool IsResolved => ElementType.IsResolved;
    public override bool IsError => ElementType.IsError;

    public OptionalSemanticType(TypeRef elementType)
    {
        ElementType = elementType;
    }

    public override bool Equals(SemanticType? other) =>
        other is OptionalSemanticType optional && ElementType.Equals(optional.ElementType);

    public override int GetHashCode() => HashCode.Combine(Kind, ElementType);

    public override string ToString() => $"{ElementType}?";
}
