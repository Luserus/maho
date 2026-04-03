using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Represents an unqualified or generic named type reference. </summary>
internal sealed class ResolvedNamedTypeReference : ResolvedTypeReference
{
    public string Name { get; }
    public int Arity { get; }
    public IReadOnlyList<ResolvedTypeReference> TypeArguments { get; }
    public override string DisplayName { get; }
    public override string SignatureKey { get; }

    public ResolvedNamedTypeReference(
        TypeSyntax syntax,
        string name,
        int arity,
        IReadOnlyList<ResolvedTypeReference> typeArguments,
        IReadOnlyList<Symbol> candidateSymbols,
        string? signatureIdentity = null)
        : base(syntax, candidateSymbols)
    {
        Name = name;
        Arity = arity;
        TypeArguments = typeArguments;
        DisplayName = CreateDisplayName(name, typeArguments);
        SignatureKey = signatureIdentity ?? CreateSignatureKey(name, arity, typeArguments);
    }

    private static string CreateDisplayName(string name, IReadOnlyList<ResolvedTypeReference> typeArguments)
    {
        if (typeArguments.Count == 0)
            return name;

        string[] parts = new string[typeArguments.Count];

        for (int i = 0; i < parts.Length; i++)
            parts[i] = typeArguments[i].DisplayName;

        return $"{name}<{string.Join(", ", parts)}>";
    }

    private static string CreateSignatureKey(string name, int arity, IReadOnlyList<ResolvedTypeReference> typeArguments)
    {
        if (typeArguments.Count == 0)
            return arity == 0 ? name : $"{name}`{arity}";

        string[] parts = new string[typeArguments.Count];

        for (int i = 0; i < parts.Length; i++)
            parts[i] = typeArguments[i].SignatureKey;

        return $"{name}`{arity}<{string.Join(",", parts)}>";
    }
}
