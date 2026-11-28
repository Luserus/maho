using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>Represents a fixed-size inline array type (<c>T[size1, size2, ...]</c>).</summary>
internal sealed class ArraySemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Array;
    public TypeRef ElementType { get; }
    public IReadOnlyList<Expression> Sizes { get; }
    public Expression Size => Sizes.Count > 0 ? Sizes[0] : null!;
    public int Dimension => Sizes.Count;

    public override bool IsResolved => ElementType.IsResolved;
    public override bool IsError => ElementType.IsError;

    public ArraySemanticType(TypeRef elementType, IReadOnlyList<Expression> sizes)
    {
        ElementType = elementType;
        Sizes = sizes ?? throw new ArgumentNullException(nameof(sizes));
    }

    public ArraySemanticType(TypeRef elementType, Expression size)
        : this(elementType, new[] { size ?? throw new ArgumentNullException(nameof(size)) })
    {
    }

    public override bool Equals(SemanticType? other)
    {
        if (other is not ArraySemanticType array)
            return false;

        if (!ElementType.Equals(array.ElementType))
            return false;

        if (Sizes.Count != array.Sizes.Count)
            return false;

        for (int i = 0; i < Sizes.Count; i++)
        {
            var sA = Sizes[i];
            var sB = array.Sizes[i];
            if (sA == sB || Nullable.Equals(sA.GetSpan(), sB.GetSpan()))
                continue;

            if (sA is LiteralExpression litA && sB is LiteralExpression litB && litA.Literal.Value == litB.Literal.Value)
                continue;

            return false;
        }

        return true;
    }

    public override int GetHashCode() => HashCode.Combine(Kind, ElementType, Sizes.Count);

    public override string ToString() => $"{ElementType}[{string.Join(", ", Sizes)}]";

    public override string ToDisplayString(ResolutionContext? context = null) =>
        $"{ElementType.ToDisplayString(context)}[{string.Join(", ", Sizes.Select(s => s is LiteralExpression lit ? lit.Literal.Value : "..."))}]";
}
