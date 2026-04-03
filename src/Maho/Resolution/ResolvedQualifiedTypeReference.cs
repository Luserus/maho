using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Represents a qualified type reference such as <c>A.B</c>. </summary>
internal sealed class ResolvedQualifiedTypeReference : ResolvedTypeReference
{
    public ResolvedTypeReference Left { get; }
    public ResolvedTypeReference Right { get; }
    public override string DisplayName { get; }
    public override string SignatureKey { get; }

    public ResolvedQualifiedTypeReference(
        QualifiedType syntax,
        ResolvedTypeReference left,
        ResolvedTypeReference right,
        IReadOnlyList<Symbol> candidateSymbols)
        : base(syntax, candidateSymbols)
    {
        Left = left;
        Right = right;
        DisplayName = $"{left.DisplayName}.{right.DisplayName}";
        SignatureKey = BuildSignatureKey(candidateSymbols, left, right);
    }

    private static string BuildSignatureKey(IReadOnlyList<Symbol> candidates, ResolvedTypeReference left, ResolvedTypeReference right)
    {
        if (candidates.Count == 1)
            return candidates[0].QualifiedMetadataName;

        return $"{left.SignatureKey}.{right.SignatureKey}";
    }
}
