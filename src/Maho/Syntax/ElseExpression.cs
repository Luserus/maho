namespace Maho.Syntax;

internal sealed class ElseExpression : Expression
{
    public Token Keyword { get; }
    public Expression Expression { get; }

    public ElseExpression(Token keyword, Expression expression)
    {
        Keyword = keyword;
        Expression = expression;
    }
}
