using System;

namespace Maho.Resolution;

/// <summary>Represents a reference type (<c>T&amp;</c>).</summary>
internal sealed class ReferenceSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Reference;
    public TypeRef ElementType { get; }

    public override bool IsResolved => ElementType.IsResolved;
    public override bool IsError => ElementType.IsError;

    public ReferenceSemanticType(TypeRef elementType)
    {
        ElementType = elementType;
    }

    public override bool Equals(SemanticType? other) =>
        other is ReferenceSemanticType reference && ElementType.Equals(reference.ElementType);

    public override int GetHashCode() => HashCode.Combine(Kind, ElementType);

    public override string ToString() => $"{ElementType}&";
}
