using System;
using System.Collections.Generic;
using System.Linq;

namespace Maho.Resolution;

/// <summary>Represents an algebraic sum/union type (<c>(A | B | C)</c> or <c>untagged (A | B)</c>).</summary>
internal sealed class UnionSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Union;
    public IReadOnlyList<TypeRef> Variants { get; }
    public bool IsUntagged { get; }

    public override bool IsResolved => Variants.Count > 0 && Variants.All(v => v.IsResolved);
    public override bool IsError => Variants.Any(v => v.IsError);

    internal UnionSemanticType(IReadOnlyList<TypeRef> variants, bool isUntagged = false)
    {
        Variants = NormalizeVariants(variants);
        IsUntagged = isUntagged;
    }

    private static List<TypeRef> NormalizeVariants(IReadOnlyList<TypeRef> inputs)
    {
        var flat = new List<TypeRef>();

        void Flatten(TypeRef typeRef)
        {
            if (typeRef.SpecialType is UnionSemanticType union)
            {
                foreach (var v in union.Variants)
                    Flatten(v);
            }
            else
                flat.Add(typeRef);
        }

        foreach (var input in inputs)
            Flatten(input);

        var distinct = new List<TypeRef>();

        foreach (var item in flat)
        {
            if (!distinct.Any(d => d.Equals(item)))
                distinct.Add(item);
        }

        distinct.Sort((a, b) => string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal));
        return distinct;
    }

    public override bool Equals(SemanticType? other)
    {
        if (other is not UnionSemanticType union || IsUntagged != union.IsUntagged || Variants.Count != union.Variants.Count)
            return false;

        for (int i = 0; i < Variants.Count; i++)
        {
            if (!Variants[i].Equals(union.Variants[i]))
                return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(IsUntagged);
        hash.Add(Variants.Count);

        foreach (var variant in Variants)
            hash.Add(variant);

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        string repr = Variants.Count == 1 ? $"({Variants[0]} |)" : $"({string.Join(" | ", Variants)})";
        return IsUntagged ? $"untagged {repr}" : repr;
    }

    public override string ToDisplayString(ResolutionContext? context = null)
    {
        string repr = Variants.Count == 1
            ? $"({Variants[0].ToDisplayString(context)} |)"
            : $"({string.Join(" | ", Variants.Select(v => v.ToDisplayString(context)))})";
        return IsUntagged ? $"untagged {repr}" : repr;
    }
}
