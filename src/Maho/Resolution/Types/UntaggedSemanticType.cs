using System;

namespace Maho.Resolution;

/// <summary>Represents an untagged modifier applied to a type reference (<c>untagged T</c>).</summary>
internal sealed class UntaggedSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Untagged;
    public TypeRef InnerType { get; }

    public override bool IsResolved => InnerType.IsResolved;
    public override bool IsError => InnerType.IsError;

    public UntaggedSemanticType(TypeRef innerType) => InnerType = innerType;

    public override bool Equals(SemanticType? other) =>
        other is UntaggedSemanticType untagged && InnerType.Equals(untagged.InnerType);

    public override int GetHashCode() => HashCode.Combine(Kind, InnerType);

    public override string ToString() => $"untagged {InnerType}";

    public override string ToDisplayString(ResolutionContext? context = null) => $"untagged {InnerType.ToDisplayString(context)}";
}
