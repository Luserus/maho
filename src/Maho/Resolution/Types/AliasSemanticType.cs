using System;

namespace Maho.Resolution;

/// <summary>
/// Represents a storage alias type (<c>&amp;T</c>, <c>mut &amp;T</c>, <c>*T</c>, or <c>mut *T</c>).
/// </summary>
internal sealed class AliasSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Alias;
    public TypeRef TargetType { get; }
    public bool IsMutable { get; }
    public bool IsUnsafe { get; }

    public override bool IsResolved => TargetType.IsResolved;
    public override bool IsError => TargetType.IsError;

    public AliasSemanticType(TypeRef targetType, bool isMutable = false, bool isUnsafe = false)
    {
        TargetType = targetType;
        IsMutable = isMutable;
        IsUnsafe = isUnsafe;
    }

    public override bool Equals(SemanticType? other) =>
        other is AliasSemanticType alias && IsMutable == alias.IsMutable && IsUnsafe == alias.IsUnsafe && TargetType.Equals(alias.TargetType);

    public override int GetHashCode() => HashCode.Combine(Kind, TargetType, IsMutable, IsUnsafe);

    public override string ToString()
    {
        string prefix = IsUnsafe ? "*" : "&";
        return IsMutable ? $"mut {prefix}{TargetType}" : $"{prefix}{TargetType}";
    }

    public override string ToDisplayString(ResolutionContext? context = null)
    {
        string prefix = IsUnsafe ? "*" : "&";
        return IsMutable ? $"mut {prefix}{TargetType.ToDisplayString(context)}" : $"{prefix}{TargetType.ToDisplayString(context)}";
    }
}

