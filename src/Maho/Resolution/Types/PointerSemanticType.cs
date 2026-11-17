using System;

namespace Maho.Resolution;

/// <summary>Represents a pointer type (<c>T*</c> or <c>T mut*</c>).</summary>
internal sealed class PointerSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Pointer;
    public TypeRef ElementType { get; }
    public bool IsMutable { get; }

    public override bool IsResolved => ElementType.IsResolved;
    public override bool IsError => ElementType.IsError;

    public PointerSemanticType(TypeRef elementType, bool isMutable = false)
    {
        ElementType = elementType;
        IsMutable = isMutable;
    }

    public override bool Equals(SemanticType? other) =>
        other is PointerSemanticType pointer && IsMutable == pointer.IsMutable && ElementType.Equals(pointer.ElementType);

    public override int GetHashCode() => HashCode.Combine(Kind, ElementType, IsMutable);

    public override string ToString() => IsMutable ? $"{ElementType} mut*" : $"{ElementType}*";

    public override string ToDisplayString(ResolutionContext? context = null) =>
        IsMutable ? $"{ElementType.ToDisplayString(context)} mut*" : $"{ElementType.ToDisplayString(context)}*";
}
