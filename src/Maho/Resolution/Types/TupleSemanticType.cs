using System;
using System.Collections.Generic;
using System.Linq;

namespace Maho.Resolution;

/// <summary>Represents an element in a tuple semantic type with an optional name.</summary>
internal readonly record struct TupleElement(TypeRef Type, string? Name = null)
{
    public override string ToString() => Name is null ? Type.ToString() : $"{Type} {Name}";
}

/// <summary>Represents a tuple type (<c>(T1, T2)</c> or <c>(T1 a, T2 b)</c>).</summary>
internal sealed class TupleSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Tuple;
    public IReadOnlyList<TupleElement> Elements { get; }

    public override bool IsResolved => Elements.Count > 0 && Elements.All(e => e.Type.IsResolved);
    public override bool IsError => Elements.Any(e => e.Type.IsError);

    public TupleSemanticType(IReadOnlyList<TupleElement> elements)
    {
        Elements = elements;
    }

    public override bool Equals(SemanticType? other)
    {
        if (other is not TupleSemanticType tuple || Elements.Count != tuple.Elements.Count)
            return false;

        for (int i = 0; i < Elements.Count; i++)
        {
            if (!Elements[i].Type.Equals(tuple.Elements[i].Type) ||
                !string.Equals(Elements[i].Name, tuple.Elements[i].Name, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Elements.Count);
        foreach (var element in Elements)
        {
            hash.Add(element.Type);
            hash.Add(element.Name);
        }
        return hash.ToHashCode();
    }

    public override string ToString() => $"({string.Join(", ", Elements)})";
}
