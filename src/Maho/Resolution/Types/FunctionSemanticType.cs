using System;
using System.Collections.Generic;
using System.Linq;

namespace Maho.Resolution;

/// <summary>Represents a first-class function pointer type: func(P1, P2, ...) -&gt; ReturnType.</summary>
internal sealed class FunctionSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Function;
    public TypeRef ReturnType { get; }
    public IReadOnlyList<TypeRef> ParameterTypes { get; }

    public override bool IsResolved => ReturnType.IsResolved && ParameterTypes.All(p => p.IsResolved);
    public override bool IsError => ReturnType.IsError || ParameterTypes.Any(p => p.IsError);

    public FunctionSemanticType(TypeRef returnType, IReadOnlyList<TypeRef> parameterTypes)
    {
        ReturnType = returnType;
        ParameterTypes = parameterTypes;
    }

    public override bool Equals(SemanticType? other)
    {
        if (other is not FunctionSemanticType fn)
            return false;

        if (!ReturnType.Equals(fn.ReturnType) || ParameterTypes.Count != fn.ParameterTypes.Count)
            return false;

        for (int i = 0; i < ParameterTypes.Count; i++)
        {
            if (!ParameterTypes[i].Equals(fn.ParameterTypes[i]))
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
        $"func({string.Join(", ", ParameterTypes)}) -> {ReturnType}";

    public override string ToDisplayString(ResolutionContext? context = null) =>
        $"func({string.Join(", ", ParameterTypes.Select(p => p.ToDisplayString(context)))}) -> {ReturnType.ToDisplayString(context)}";
}
