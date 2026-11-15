using System;
using System.Collections.Generic;
using System.Linq;

namespace Maho.Resolution;

/// <summary>Represents a dynamic existential type reference (<c>dyn T</c> or <c>dyn (A + B)</c>).</summary>
internal sealed class DynSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Dyn;
    public TypeRef TargetType { get; }
    public IReadOnlyList<TypeRef> ComposedTypes { get; }

    public override bool IsResolved => TargetType.IsResolved && ComposedTypes.All(t => t.IsResolved);
    public override bool IsError => TargetType.IsError || ComposedTypes.Any(t => t.IsError);

    public DynSemanticType(TypeRef targetType, IReadOnlyList<TypeRef>? composedTypes = null)
    {
        TargetType = targetType;
        ComposedTypes = composedTypes ?? Array.Empty<TypeRef>();
    }

    public override bool Equals(SemanticType? other)
    {
        if (other is not DynSemanticType dyn)
            return false;

        if (!TargetType.Equals(dyn.TargetType))
            return false;

        if (ComposedTypes.Count != dyn.ComposedTypes.Count)
            return false;

        for (int i = 0; i < ComposedTypes.Count; i++)
        {
            if (!ComposedTypes[i].Equals(dyn.ComposedTypes[i]))
                return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(TargetType);
        foreach (var c in ComposedTypes)
            hash.Add(c);
        return hash.ToHashCode();
    }

    public override string ToString()
    {
        if (ComposedTypes.Count == 0)
            return $"dyn {TargetType}";
        return $"dyn ({TargetType} + {string.Join(" + ", ComposedTypes)})";
    }

    public override string ToDisplayString(ResolutionContext? context = null)
    {
        if (ComposedTypes.Count == 0)
            return $"dyn {TargetType.ToDisplayString(context)}";
        return $"dyn ({TargetType.ToDisplayString(context)} + {string.Join(" + ", ComposedTypes.Select(t => t.ToDisplayString(context)))})";
    }
}

