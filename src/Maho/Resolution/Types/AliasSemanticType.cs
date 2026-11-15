using System;

namespace Maho.Resolution;

/// <summary>
/// Represents a storage alias type (<c>ref T</c> or <c>mut ref T</c>).
/// </summary>
internal sealed class AliasSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Alias;
    public TypeRef TargetType { get; }
    public bool IsMutable { get; }

    public override bool IsResolved => TargetType.IsResolved;
    public override bool IsError => TargetType.IsError;

    public AliasSemanticType(TypeRef targetType, bool isMutable = false)
    {
        TargetType = targetType;
        IsMutable = isMutable;
    }

    public override bool Equals(SemanticType? other) =>
        other is AliasSemanticType alias && IsMutable == alias.IsMutable && TargetType.Equals(alias.TargetType);

    public override int GetHashCode() => HashCode.Combine(Kind, TargetType, IsMutable);

    public override string ToString() => IsMutable ? $"mut ref {TargetType}" : $"ref {TargetType}";

    public override string ToDisplayString(ResolutionContext? context = null) =>
        IsMutable ? $"mut ref {TargetType.ToDisplayString(context)}" : $"ref {TargetType.ToDisplayString(context)}";
}

