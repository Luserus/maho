using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Base semantic model for declaration-site type syntax after the first pass interprets it. </summary>
internal abstract class ResolvedTypeReference
{
    /// <summary> Creates one semantic type-reference object from parser type syntax. </summary>
    protected ResolvedTypeReference(TypeSyntax syntax, IReadOnlyList<Symbol> candidateSymbols)
    {
        Syntax = syntax;
        CandidateSymbols = candidateSymbols;
    }

    /// <summary> Original parser type syntax this semantic model came from. </summary>
    public TypeSyntax Syntax { get; }
    /// <summary> Candidate declarations that matched this reference during first-pass lookup. </summary>
    public IReadOnlyList<Symbol> CandidateSymbols { get; }
    /// <summary> Human-readable display form used for diagnostics/debugging. </summary>
    public abstract string DisplayName { get; }
    /// <summary> Stable semantic signature form used for later comparison and caching. </summary>
    public abstract string SignatureKey { get; }
}
