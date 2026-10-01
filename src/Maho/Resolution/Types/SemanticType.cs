using System;

namespace Maho.Resolution;

/// <summary>Represents a non-symbol, compiler-constructed structural semantic type.</summary>
internal abstract class SemanticType : IEquatable<SemanticType>
{
    public abstract SemanticTypeKind Kind { get; }
    public abstract bool IsResolved { get; }
    public abstract bool IsError { get; }

    public abstract bool Equals(SemanticType? other);

    public override bool Equals(object? obj) => obj is SemanticType other && Equals(other);

    public abstract override int GetHashCode();

    public abstract override string ToString();
}
