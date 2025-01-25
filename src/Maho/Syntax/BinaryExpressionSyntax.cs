namespace Maho.Syntax;

/// <summary> Represents a binary expression node. </summary>
internal sealed class BinaryExpressionSyntax : ExpressionSyntax
{
    /// <summary> The Left-Hand-Side expression. </summary>
    public ExpressionSyntax LeftExpression { get; }
    /// <summary> The binary operator. </summary>
    public Token OperatorToken { get; }
    /// <summary> The Right-Hand-Side expression. </summary>
    public ExpressionSyntax RightExpression { get; }

    /// <param name="leftExpression"> The Left-Hand-Side expression. </param>
    /// <param name="operatorToken"> The binary operator. </param>
    /// <param name="rightExpression"> The Right-Hand-Side expression. </param>
    public BinaryExpressionSyntax(ExpressionSyntax leftExpression, Token operatorToken, ExpressionSyntax rightExpression)
    {
        LeftExpression = leftExpression;
        OperatorToken = operatorToken;
        RightExpression = rightExpression;
    }
}