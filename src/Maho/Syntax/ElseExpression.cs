namespace Maho.Syntax;

internal sealed class ElseExpression : Expression
{
    public Token ElseKeyword { get; }
    public Expression Expression { get; }

    public ElseExpression(Token elseKeyword, Expression expression)
    {
        ElseKeyword = elseKeyword;
        Expression = expression;
    }
}
