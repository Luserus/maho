namespace Maho.Syntax;

internal sealed class IfExpression : Expression
{
    public Token IfKeyword { get; }
    public Token OpenParen { get; }
    public Expression Condition { get; }
    public Token CloseParen { get; }
    public Expression ThenExpression { get; }
    public ElseExpression? ElseExpression { get; }

    public IfExpression(Token ifKeyword, Token openParen, Expression condition, Token closeParen, Expression thenExpression, ElseExpression? elseExpression)
    {
        IfKeyword = ifKeyword;
        OpenParen = openParen;
        Condition = condition;
        CloseParen = closeParen;
        ThenExpression = thenExpression;
        ElseExpression = elseExpression;
    }
}
