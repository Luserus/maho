using System;
using System.Collections.Generic;
using System.Linq;

namespace Maho.Resolution;

/// <summary>Represents an instantiated generic type (e.g. <c>Function&lt;Unit&gt;</c>, <c>List&lt;Int32&gt;</c>).</summary>
internal sealed class GenericSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Generic;
    public SymbolHandle TargetHandle { get; }
    public string TargetName { get; }
    public IReadOnlyList<TypeRef> TypeArguments { get; }

    public override bool IsResolved => TypeArguments.All(a => a.IsResolved);
    public override bool IsError => TypeArguments.Any(a => a.IsError);

    public GenericSemanticType(SymbolHandle targetHandle, string targetName, IReadOnlyList<TypeRef> typeArguments)
    {
        TargetHandle = targetHandle;
        TargetName = targetName;
        TypeArguments = typeArguments;
    }

    public override bool Equals(SemanticType? other)
    {
        if (other is not GenericSemanticType gen)
            return false;

        if (TargetHandle != gen.TargetHandle || TypeArguments.Count != gen.TypeArguments.Count)
            return false;

        for (int i = 0; i < TypeArguments.Count; i++)
        {
            if (!TypeArguments[i].Equals(gen.TypeArguments[i]))
                return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(TargetHandle);
        foreach (var arg in TypeArguments)
            hash.Add(arg);
        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"{TargetName}<{string.Join(", ", TypeArguments)}>";

    public override string ToDisplayString(ResolutionContext? context = null) =>
        $"{TargetName}<{string.Join(", ", TypeArguments.Select(a => a.ToDisplayString(context)))}>";
}

