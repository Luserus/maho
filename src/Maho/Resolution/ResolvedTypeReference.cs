using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Base semantic model for declaration-site type syntax after the first pass interprets it. </summary>
internal abstract class ResolvedTypeReference
{
    protected ResolvedTypeReference(TypeSyntax syntax, IReadOnlyList<Symbol> candidateSymbols)
    {
        Syntax = syntax;
        CandidateSymbols = candidateSymbols;
    }

    public TypeSyntax Syntax { get; }
    /// <summary> Candidate declarations that matched this reference during first-pass lookup. </summary>
    public IReadOnlyList<Symbol> CandidateSymbols { get; }
    public abstract string DisplayName { get; }
    public abstract string SignatureKey { get; }
}
