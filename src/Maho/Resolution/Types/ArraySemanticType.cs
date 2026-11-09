using System;

namespace Maho.Resolution;

/// <summary>Represents a dynamic array type (<c>T[]</c>).</summary>
internal sealed class ArraySemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Array;
    public TypeRef ElementType { get; }

    public override bool IsResolved => ElementType.IsResolved;
    public override bool IsError => ElementType.IsError;

    public ArraySemanticType(TypeRef elementType) => ElementType = elementType;

    public override bool Equals(SemanticType? other) =>
        other is ArraySemanticType array && ElementType.Equals(array.ElementType);

    public override int GetHashCode() => HashCode.Combine(Kind, ElementType);

    public override string ToString() => $"{ElementType}[]";

    public override string ToDisplayString(ResolutionContext? context = null) => $"{ElementType.ToDisplayString(context)}[]";
}
