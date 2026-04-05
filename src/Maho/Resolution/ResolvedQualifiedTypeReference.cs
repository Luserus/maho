using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Represents a qualified type reference such as <c>A.B</c>. </summary>
internal sealed class ResolvedQualifiedTypeReference : ResolvedTypeReference
{
    /// <summary> Cached display name built only if a consumer needs the human-readable form. </summary>
    private string? displayName;
    /// <summary> Cached signature key built only if a semantic comparison actually needs it. </summary>
    private string? signatureKey;

    /// <summary> Left side of the qualification chain. </summary>
    public ResolvedTypeReference Left { get; }
    /// <summary> Right side of the qualification chain. </summary>
    public ResolvedTypeReference Right { get; }
    /// <summary> Human-readable qualified display form. </summary>
    public override string DisplayName => displayName ??= $"{Left.DisplayName}.{Right.DisplayName}";
    /// <summary> Stable semantic signature form for the qualified reference. </summary>
    public override string SignatureKey => signatureKey ??= BuildSignatureKey(CandidateSymbols, Left, Right);

    /// <summary> Creates a semantic model for a qualified type reference such as <c>A.B</c>. </summary>
    public ResolvedQualifiedTypeReference(
        QualifiedType syntax,
        ResolvedTypeReference left,
        ResolvedTypeReference right,
        IReadOnlyList<Symbol> candidateSymbols)
        : base(syntax, candidateSymbols)
    {
        Left = left;
        Right = right;
    }

    /// <summary>
    /// Prefers an actual resolved candidate's fully qualified metadata name when lookup was
    /// unambiguous; otherwise falls back to composing the left/right signature chain.
    /// </summary>
    private static string BuildSignatureKey(IReadOnlyList<Symbol> candidates, ResolvedTypeReference left, ResolvedTypeReference right)
    {
        if (candidates.Count == 1)
            return candidates[0].QualifiedMetadataName;

        return $"{left.SignatureKey}.{right.SignatureKey}";
    }
}
