using System;
using System.Collections.Generic;
using System.Linq;

namespace Maho.Resolution;

/// <summary>Represents a first-class delegate type: delegate(P1, P2, ...) -&gt; ReturnType.</summary>
internal sealed class DelegateSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Delegate;
    public TypeRef ReturnType { get; }
    public IReadOnlyList<TypeRef> ParameterTypes { get; }

    public override bool IsResolved => ReturnType.IsResolved && ParameterTypes.All(p => p.IsResolved);
    public override bool IsError => ReturnType.IsError || ParameterTypes.Any(p => p.IsError);

    public DelegateSemanticType(TypeRef returnType, IReadOnlyList<TypeRef> parameterTypes)
    {
        ReturnType = returnType;
        ParameterTypes = parameterTypes;
    }

    public override bool Equals(SemanticType? other)
    {
        if (other is not DelegateSemanticType del)
            return false;

        if (!ReturnType.Equals(del.ReturnType) || ParameterTypes.Count != del.ParameterTypes.Count)
            return false;

        for (int i = 0; i < ParameterTypes.Count; i++)
        {
            if (!ParameterTypes[i].Equals(del.ParameterTypes[i]))
                return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(ReturnType);
        foreach (var p in ParameterTypes)
            hash.Add(p);
        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"delegate({string.Join(", ", ParameterTypes)}) -> {ReturnType}";

    public override string ToDisplayString(ResolutionContext? context = null) =>
        $"delegate({string.Join(", ", ParameterTypes.Select(p => p.ToDisplayString(context)))}) -> {ReturnType.ToDisplayString(context)}";
}
