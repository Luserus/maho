using System;

namespace Maho.Resolution;

/// <summary>Represents the semantic state of a type reference in the compiler.</summary>
internal enum TypeRefKind : byte
{
    /// <summary>The type reference has not yet been processed by the resolution pass.</summary>
    Unresolved = 0,
    /// <summary>The type reference is omitted or 'var', pending expression-level type inference.</summary>
    Inferred,
    /// <summary>The type reference failed to resolve or is syntactically invalid.</summary>
    Error,
    /// <summary>The type reference was successfully resolved to a concrete symbol.</summary>
    Resolved
}

/// <summary>A full-fidelity representation of a type reference across symbols and declarations.</summary>
internal readonly struct TypeRef : IEquatable<TypeRef>
{
    public TypeRefKind Kind { get; }
    public SymbolHandle? Handle { get; }

    public bool IsResolved => Kind == TypeRefKind.Resolved;
    public bool IsUnresolved => Kind == TypeRefKind.Unresolved;
    public bool IsInferred => Kind == TypeRefKind.Inferred;
    public bool IsError => Kind == TypeRefKind.Error;

    private TypeRef(TypeRefKind kind, SymbolHandle? handle)
    {
        Kind = kind;
        Handle = handle;
    }

    public static TypeRef Unresolved => new(TypeRefKind.Unresolved, null);
    public static TypeRef Inferred => new(TypeRefKind.Inferred, null);
    public static TypeRef Error => new(TypeRefKind.Error, null);
    public static TypeRef Resolved(SymbolHandle handle) => new(TypeRefKind.Resolved, handle);

    public static implicit operator TypeRef(SymbolHandle handle) => Resolved(handle);

    public bool Equals(TypeRef other) => Kind == other.Kind && Nullable.Equals(Handle, other.Handle);
    public override bool Equals(object? obj) => obj is TypeRef other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Kind, Handle);

    public static bool operator ==(TypeRef left, TypeRef right) => left.Equals(right);
    public static bool operator !=(TypeRef left, TypeRef right) => !left.Equals(right);

    public static bool operator ==(TypeRef left, SymbolHandle right) => left.Kind == TypeRefKind.Resolved && left.Handle == right;
    public static bool operator !=(TypeRef left, SymbolHandle right) => !(left == right);

    public static bool operator ==(SymbolHandle left, TypeRef right) => right == left;
    public static bool operator !=(SymbolHandle left, TypeRef right) => !(right == left);

    public override string ToString() => Kind switch
    {
        TypeRefKind.Resolved => Handle.ToString() ?? "Resolved",
        TypeRefKind.Inferred => "var",
        TypeRefKind.Error => "<error>",
        _ => "<unresolved>"
    };
}
