using System;

namespace Maho.Resolution;

/// <summary>Represents a dynamically sized slice type (<c>T[]</c>, <c>T[,]</c>, etc.).</summary>
internal sealed class SliceSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Slice;
    public TypeRef ElementType { get; }
    public int Dimension { get; }

    public override bool IsResolved => ElementType.IsResolved;
    public override bool IsError => ElementType.IsError;

    public SliceSemanticType(TypeRef elementType, int dimension = 1)
    {
        ElementType = elementType;
        Dimension = dimension;
    }

    public override bool Equals(SemanticType? other) =>
        other is SliceSemanticType slice && ElementType.Equals(slice.ElementType) && Dimension == slice.Dimension;

    public override int GetHashCode() => HashCode.Combine(Kind, ElementType, Dimension);

    public override string ToString() => Dimension == 1 ? $"{ElementType}[]" : $"{ElementType}[{new string(',', Dimension - 1)}]";

    public override string ToDisplayString(ResolutionContext? context = null) =>
        Dimension == 1 ? $"{ElementType.ToDisplayString(context)}[]" : $"{ElementType.ToDisplayString(context)}[{new string(',', Dimension - 1)}]";
}

