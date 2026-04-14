using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>
/// Represents one special unqualified signature type form that bypasses normal lookup today, such as
/// <c>dyn</c> or deferred return-position <c>var</c>.
/// </summary>
internal sealed class ResolvedKeywordTypeReference : ResolvedTypeReference
{
    /// <summary> Source text carried by the special signature reference. </summary>
    public string Keyword { get; }
    /// <summary> Human-readable display form used for diagnostics and debug output. </summary>
    public override string DisplayName => Keyword;
    /// <summary> Stable signature identity used by later semantic passes. </summary>
    public override string SignatureKey => Keyword;

    /// <summary> Creates a semantic model for one special unqualified signature type form. </summary>
    public ResolvedKeywordTypeReference(TypeSyntax syntax, string keyword)
        : base(syntax, [])
    {
        Keyword = keyword;
    }
}
