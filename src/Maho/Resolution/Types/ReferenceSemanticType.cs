using System;

namespace Maho.Resolution;

/// <summary>Represents a reference type (<c>T&amp;</c> or <c>T mut&amp;</c>).</summary>
internal sealed class ReferenceSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Reference;
    public TypeRef ElementType { get; }
    public bool IsMutable { get; }

    public override bool IsResolved => ElementType.IsResolved;
    public override bool IsError => ElementType.IsError;

    public ReferenceSemanticType(TypeRef elementType, bool isMutable = false)
    {
        ElementType = elementType;
        IsMutable = isMutable;
    }

    public override bool Equals(SemanticType? other) =>
        other is ReferenceSemanticType reference && IsMutable == reference.IsMutable && ElementType.Equals(reference.ElementType);

    public override int GetHashCode() => HashCode.Combine(Kind, ElementType, IsMutable);

    public override string ToString() => IsMutable ? $"{ElementType} mut&" : $"{ElementType}&";

    public override string ToDisplayString(ResolutionContext? context = null) =>
        IsMutable ? $"{ElementType.ToDisplayString(context)} mut&" : $"{ElementType.ToDisplayString(context)}&";
}
