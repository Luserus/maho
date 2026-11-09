global using SymbolHandle = (Maho.Resolution.SymbolKind Kind, Maho.Resolution.SymbolID ID);

using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>A full-fidelity representation of a type reference across symbols and declarations.</summary>
internal readonly struct TypeRef : IEquatable<TypeRef>
{
    public TypeRefKind Kind { get; }
    public SymbolHandle? Handle { get; }
    public SemanticType? SpecialType { get; }

    public bool IsSpecial => SpecialType is not null;

    public bool IsResolved => Kind switch
    {
        TypeRefKind.Resolved => true,
        TypeRefKind.Pointer or TypeRefKind.Reference or TypeRefKind.Array or
        TypeRefKind.FixedArray or TypeRefKind.Span or TypeRefKind.Optional or TypeRefKind.Tuple or
        TypeRefKind.Union or TypeRefKind.Untagged => SpecialType is not null && SpecialType.IsResolved,
        _ => false
    };

    public bool IsUnresolved => Kind switch
    {
        TypeRefKind.Unresolved => true,
        TypeRefKind.Pointer or TypeRefKind.Reference or TypeRefKind.Array or
        TypeRefKind.FixedArray or TypeRefKind.Span or TypeRefKind.Optional or TypeRefKind.Tuple or
        TypeRefKind.Union or TypeRefKind.Untagged => SpecialType is not null && !SpecialType.IsResolved && !SpecialType.IsError,
        _ => false
    };

    public bool IsInferred => Kind == TypeRefKind.Inferred;

    public bool IsError => Kind == TypeRefKind.Error || (SpecialType is not null && SpecialType.IsError);

    private TypeRef(TypeRefKind kind, SymbolHandle? handle, SemanticType? specialType = null)
    {
        Kind = kind;
        Handle = handle;
        SpecialType = specialType;
    }

    public static TypeRef Unresolved => new TypeRef(TypeRefKind.Unresolved, null);
    public static TypeRef Inferred => new TypeRef(TypeRefKind.Inferred, null);
    public static TypeRef Error => new TypeRef(TypeRefKind.Error, null);
    public static TypeRef Resolved(SymbolHandle handle) => new TypeRef(TypeRefKind.Resolved, handle);

    public static TypeRef Pointer(TypeRef element) =>
        new TypeRef(TypeRefKind.Pointer, null, new PointerSemanticType(element));

    public static TypeRef Reference(TypeRef element) =>
        new TypeRef(TypeRefKind.Reference, null, new ReferenceSemanticType(element));

    public static TypeRef Array(TypeRef element) =>
        new TypeRef(TypeRefKind.Array, null, new ArraySemanticType(element));

    public static TypeRef FixedArray(TypeRef element, Expression size) =>
        new TypeRef(TypeRefKind.FixedArray, null, new FixedArraySemanticType(element, size));

    public static TypeRef Span(TypeRef element) =>
        new TypeRef(TypeRefKind.Span, null, new SpanSemanticType(element));

    public static TypeRef Optional(TypeRef element) =>
        new TypeRef(TypeRefKind.Optional, null, new OptionalSemanticType(element));

    public static TypeRef Tuple(IReadOnlyList<TupleElement> elements) =>
        new TypeRef(TypeRefKind.Tuple, null, new TupleSemanticType(elements));

    public static TypeRef Union(IReadOnlyList<TypeRef> constituents, bool isUntagged = false)
    {
        var semantic = new UnionSemanticType(constituents, isUntagged);
        return new TypeRef(TypeRefKind.Union, null, semantic);
    }

    public static TypeRef Untagged(TypeRef inner)
    {
        if (inner.SpecialType is UnionSemanticType u)
            return Union(u.Variants, isUntagged: true);

        if (inner.Kind == TypeRefKind.Untagged)
            return inner;

        return new TypeRef(TypeRefKind.Untagged, null, new UntaggedSemanticType(inner));
    }

    public bool IsUntagged => Kind == TypeRefKind.Untagged || (SpecialType is UnionSemanticType u && u.IsUntagged);

    public static implicit operator TypeRef(SymbolHandle handle) => Resolved(handle);

    public bool Equals(TypeRef other)
    {
        if (Kind != other.Kind)
            return false;

        if (IsSpecial)
            return Equals(SpecialType, other.SpecialType);

        return Nullable.Equals(Handle, other.Handle);
    }

    public override bool Equals(object? obj) => obj is TypeRef other && Equals(other);

    public override int GetHashCode() => IsSpecial ? HashCode.Combine(Kind, SpecialType) : HashCode.Combine(Kind, Handle);

    public static bool operator ==(TypeRef left, TypeRef right) => left.Equals(right);
    public static bool operator !=(TypeRef left, TypeRef right) => !left.Equals(right);

    public static bool operator ==(TypeRef left, SymbolHandle right) => left.Kind == TypeRefKind.Resolved && left.Handle == right;
    public static bool operator !=(TypeRef left, SymbolHandle right) => !(left == right);

    public static bool operator ==(SymbolHandle left, TypeRef right) => right == left;
    public static bool operator !=(SymbolHandle left, TypeRef right) => !(right == left);

    /// <summary>
    /// Gets the underlying type <see cref="SymbolHandle"/> (global or nested) referenced by this <see cref="TypeRef"/>,
    /// unwrapping any aliases transitively. Returns <c>null</c> if unresolved, a special semantic type, or not a type.
    /// </summary>
    public SymbolHandle? GetType(ResolutionContext context) => context.GetType(this);

    /// <summary>
    /// Unwraps any aliases transitively, returning the underlying <see cref="TypeRef"/>.
    /// </summary>
    public TypeRef UnwrapAlias(ResolutionContext context) => context.UnwrapAlias(this);

    public bool IsAssignableFrom(TypeRef source, ResolutionContext context)
    {
        if (Equals(source))
            return true;

        if (SpecialType is UnionSemanticType targetUnion)
        {
            if (source.SpecialType is UnionSemanticType sourceUnion)
                return sourceUnion.Variants.All(sv => targetUnion.Variants.Any(tv => tv.IsAssignableFrom(sv, context)));

            return targetUnion.Variants.Any(v => v.IsAssignableFrom(source, context));
        }

        if (Handle is { } thisHandle && context.GetTypeSymbol(thisHandle) is ISumTypeSymbol sumType && sumType.UnionConstituents.Count > 0)
        {
            return sumType.UnionConstituents.Any(c => c.IsAssignableFrom(source, context));
        }

        return false;
    }

    public override string ToString() => Kind switch
    {
        TypeRefKind.Resolved => Handle.ToString() ?? "Resolved",
        TypeRefKind.Inferred => "var",
        TypeRefKind.Error => "<error>",
        TypeRefKind.Pointer or TypeRefKind.Reference or TypeRefKind.Array or
        TypeRefKind.FixedArray or TypeRefKind.Span or TypeRefKind.Optional or TypeRefKind.Tuple or
        TypeRefKind.Union or TypeRefKind.Untagged => SpecialType?.ToString() ?? Kind.ToString(),
        _ => "<unresolved>"
    };

    public string ToDisplayString(ResolutionContext? context = null)
    {
        if (context is not null && Kind == TypeRefKind.Resolved && Handle is { } handle)
        {
            if (context.GetTypeSymbol(handle) is { } typeSym)
                return typeSym.Name.Text;
            if (context.GetGenericParameterSymbol(handle) is { } gpSym)
                return gpSym.Name.Text;
            if (context.GetAliasSymbol(handle) is { } aliasSym)
                return aliasSym.Name.Text;
        }

        if (SpecialType is not null)
            return SpecialType.ToDisplayString(context);

        return ToString();
    }
}