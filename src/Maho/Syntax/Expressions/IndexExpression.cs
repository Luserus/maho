namespace Maho.Syntax;

/// <summary> Indexing expression that reads from a subscripting operation. </summary>
internal sealed class IndexExpression : Expression
{
    /// <summary> Expression being indexed. </summary>
    public Expression Expression { get; }
    /// <summary> Opening bracket token. </summary>
    public Token OpenBracketToken { get; }
    /// <summary> Index expression. </summary>
    public Expression Index { get; }
    /// <summary> Closing bracket token. </summary>
    public Token CloseBracketToken { get; }

    /// <summary> Creates one index expression node. </summary>
    public IndexExpression(Expression expression, Token openBracketToken, Expression index, Token closeBracketToken)
    {
        Expression = expression;
        OpenBracketToken = openBracketToken;
        Index = index;
        CloseBracketToken = closeBracketToken;
    }
}
