using System;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>Represents a fixed-size array type (<c>T[size]</c>).</summary>
internal sealed class ArraySemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Array;
    public TypeRef ElementType { get; }
    public Expression Size { get; }
    public bool IsFixedSize => true;

    public override bool IsResolved => ElementType.IsResolved;
    public override bool IsError => ElementType.IsError;

    public ArraySemanticType(TypeRef elementType, Expression size)
    {
        ElementType = elementType;
        Size = size ?? throw new ArgumentNullException(nameof(size));
    }

    public override bool Equals(SemanticType? other)
    {
        if (other is not ArraySemanticType array)
            return false;

        if (!ElementType.Equals(array.ElementType))
            return false;

        return Size == array.Size || Nullable.Equals(Size.GetSpan(), array.Size.GetSpan());
    }

    public override int GetHashCode() => HashCode.Combine(Kind, ElementType);

    public override string ToString() => $"{ElementType}[{Size}]";
}
