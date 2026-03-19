namespace Maho.Syntax;

internal sealed class IndexExpression : Expression
{
    public Expression Expression { get; }
    public Token OpenBracketToken { get; }
    public Expression Index { get; }
    public Token CloseBracketToken { get; }

    public IndexExpression(Expression expression, Token openBracketToken, Expression index, Token closeBracketToken)
    {
        Expression = expression;
        OpenBracketToken = openBracketToken;
        Index = index;
        CloseBracketToken = closeBracketToken;
    }
}