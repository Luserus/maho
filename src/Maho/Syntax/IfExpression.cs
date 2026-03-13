namespace Maho.Syntax;

internal sealed class IfExpression : Expression
{
    public Token Keyword { get; }
    public Token OpenParen { get; }
    public Expression Condition { get; }
    public Token CloseParen { get; }
    public Expression ThenExpression { get; }
    public ElseExpression? ElseExpression { get; }

    public IfExpression(Token keyword, Token openParen, Expression condition, Token closeParen, Expression thenExpression, ElseExpression? elseExpression)
    {
        Keyword = keyword;
        OpenParen = openParen;
        Condition = condition;
        CloseParen = closeParen;
        ThenExpression = thenExpression;
        ElseExpression = elseExpression;
    }
}
