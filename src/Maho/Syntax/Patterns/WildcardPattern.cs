namespace Maho.Syntax;

/// <summary> Wildcard pattern (<c>_</c>). </summary>
internal sealed class WildcardPattern : PatternSyntax
{
    public Token Underscore { get; }

    public WildcardPattern(Token underscore)
    {
        Underscore = underscore;
    }
}

