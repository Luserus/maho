using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Represents an unqualified or generic named type reference. </summary>
internal sealed class ResolvedNamedTypeReference : ResolvedTypeReference
{
    /// <summary> Cached display name built only if a consumer needs the human-readable form. </summary>
    private string? displayName;
    /// <summary> Explicit signature identity supplied by the caller when one already exists. </summary>
    private readonly string? explicitSignatureKey;
    /// <summary> Cached signature key built only if a semantic comparison actually needs it. </summary>
    private string? signatureKey;

    /// <summary> Simple source name used by the reference before qualification is considered. </summary>
    public string Name { get; }
    /// <summary> Generic arity implied by the source form. </summary>
    public int Arity { get; }
    /// <summary> Already-resolved type arguments for generic references. </summary>
    public IReadOnlyList<ResolvedTypeReference> TypeArguments { get; }
    /// <summary> Human-readable display form, including rendered type arguments when present. </summary>
    public override string DisplayName => displayName ??= CreateDisplayName(Name, TypeArguments);
    /// <summary> Stable signature identity used by later semantic passes. </summary>
    public override string SignatureKey => signatureKey ??= explicitSignatureKey ?? CreateSignatureKey(Name, Arity, TypeArguments);

    /// <summary> Creates a semantic model for a simple or generic named type reference. </summary>
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
        explicitSignatureKey = signatureIdentity;
    }

    /// <summary> Builds the user-facing display form for diagnostics and debug output. </summary>
    private static string CreateDisplayName(string name, IReadOnlyList<ResolvedTypeReference> typeArguments)
    {
        if (typeArguments.Count == 0)
            return name;

        string[] parts = new string[typeArguments.Count];

        for (int i = 0; i < parts.Length; i++)
            parts[i] = typeArguments[i].DisplayName;

        return $"{name}<{string.Join(", ", parts)}>";
    }

    /// <summary> Builds the normalized signature identity used for semantic comparisons. </summary>
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
