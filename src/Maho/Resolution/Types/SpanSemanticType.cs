using System;

namespace Maho.Resolution;

/// <summary>Represents an unsized span type (<c>T[]</c>).</summary>
internal sealed class SpanSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Span;
    public TypeRef ElementType { get; }

    public override bool IsResolved => ElementType.IsResolved;
    public override bool IsError => ElementType.IsError;

    public SpanSemanticType(TypeRef elementType)
    {
        ElementType = elementType;
    }

    public override bool Equals(SemanticType? other) =>
        other is SpanSemanticType span && ElementType.Equals(span.ElementType);

    public override int GetHashCode() => HashCode.Combine(Kind, ElementType);

    public override string ToString() => $"{ElementType}[]";
}
