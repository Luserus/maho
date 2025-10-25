namespace Maho.Syntax;

internal sealed class ParenthesizedExpression : Expression
{
    /// <summary> The left parenthesis token. </summary>
    public Token LeftParen { get; }
    /// <summary> The expression inside the parentheses. </summary>
    public Expression Expression { get; }
    /// <summary> The right parenthesis token. </summary>
    public Token RightParen { get; }

    /// <summary> Initializes the ParenthesizedExpression class. </summary>
    /// <param name="leftParen"> The left parenthesis token. </param>
    /// <param name="expression"> The expression inside the parentheses. </param>
    /// <param name="rightParen"> The right parenthesis token. </param>
    public ParenthesizedExpression(Token leftParen, Expression expression, Token rightParen)
    {
        LeftParen = leftParen;
        Expression = expression;
        RightParen = rightParen;
    }
}