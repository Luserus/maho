using System;
using System.Collections.Generic;
using System.Linq;

namespace Maho.Resolution.Ownership;

/// <summary>
/// A named storage location and optional projections.  A place is deliberately independent of
/// syntax: it is the unit used by move, borrow, and drop analyses.
/// </summary>
internal readonly struct Place : IEquatable<Place>
{
    /// <summary>
    /// The declared storage symbol.  This is intentionally a symbol object rather than a
    /// <see cref="SymbolHandle"/>: local handles are indexed within their owning function and
    /// therefore are not compilation-global identities.
    /// </summary>
    public Symbol Root { get; }
    public IReadOnlyList<PlaceProjection> Projections { get; }

    public Place(Symbol root, IReadOnlyList<PlaceProjection>? projections = null)
    {
        Root = root;
        Projections = projections ?? Array.Empty<PlaceProjection>();
    }

    /// <summary>Extends this place with a field projection while retaining the same storage root.</summary>
    public Place ProjectField(SymbolHandle field) =>
        new(Root, [.. Projections, PlaceProjection.ForField(field)]);

    /// <summary>
    /// Extends this place with an index projection. The index expression itself is evaluated by the
    /// lowerer before this abstract, index-value-independent projection is created.
    /// </summary>
    public Place ProjectIndex() =>
        new(Root, [.. Projections, PlaceProjection.Index()]);

    /// <summary>Extends this place through a safe reference dereference.</summary>
    public Place Dereference() =>
        new(Root, [.. Projections, PlaceProjection.Dereference()]);

    public bool Equals(Place other) =>
        Root == other.Root && Projections.SequenceEqual(other.Projections);

    public override bool Equals(object? obj) => obj is Place other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Root);
        foreach (var projection in Projections)
            hash.Add(projection);
        return hash.ToHashCode();
    }

    public override string ToString() => Projections.Count == 0
        ? Root?.ToString() ?? "<invalid-place>"
        : $"{Root}{string.Concat(Projections.Select(static p => p.ToString()))}";
}

/// <summary>One field, index, or dereference step below a place root.</summary>
internal readonly record struct PlaceProjection(PlaceProjectionKind Kind, SymbolHandle? Field)
{
    public static PlaceProjection ForField(SymbolHandle field) => new(PlaceProjectionKind.Field, field);
    public static PlaceProjection Index() => new(PlaceProjectionKind.Index, null);
    public static PlaceProjection Dereference() => new(PlaceProjectionKind.Dereference, null);

    public override string ToString() => Kind switch
    {
        PlaceProjectionKind.Field => $".{Field}",
        PlaceProjectionKind.Index => "[]",
        PlaceProjectionKind.Dereference => "*",
        _ => string.Empty
    };
}

internal enum PlaceProjectionKind : byte
{
    Field,
    Index,
    Dereference
}
