namespace Maho.Syntax;

/// <summary> Represents a single arm in a match expression: <c>pattern =&gt; expression</c>. </summary>
internal sealed class MatchArm : SyntaxNode
{
    public PatternSyntax Pattern { get; }
    public Token ArrowToken { get; }
    public Expression Expression { get; }
    public Token? CommaToken { get; }

    public MatchArm(PatternSyntax pattern, Token arrowToken, Expression expression, Token? commaToken = null)
    {
        Pattern = pattern;
        ArrowToken = arrowToken;
        Expression = expression;
        CommaToken = commaToken;
    }
}

