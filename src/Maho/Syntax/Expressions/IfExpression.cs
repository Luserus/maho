namespace Maho.Syntax;

/// <summary> Expression-form if/else construct. </summary>
internal sealed class IfExpression : Expression
{
    /// <summary> The if keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }
    /// <summary> Condition expression. </summary>
    public Expression Condition { get; }
    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }
    /// <summary> Then-branch expression. </summary>
    public Expression ThenExpression { get; }
    /// <summary> Optional else branch expression. </summary>
    public ElseExpression? ElseExpression { get; }

    /// <summary> Creates one if-expression node. </summary>
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
