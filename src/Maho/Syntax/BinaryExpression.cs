namespace Maho.Syntax;

/// <summary> Represents a binary expression node. </summary>
internal sealed class BinaryExpression : Expression
{
    /// <summary> The Left-Hand-Side expression. </summary>
    public Expression LeftExpression { get; }
    /// <summary> The binary operator. </summary>
    public Token OperatorToken { get; }
    /// <summary> The Right-Hand-Side expression. </summary>
    public Expression RightExpression { get; }

    /// <param name="leftExpression"> The Left-Hand-Side expression. </param>
    /// <param name="operatorToken"> The binary operator. </param>
    /// <param name="rightExpression"> The Right-Hand-Side expression. </param>
    public BinaryExpression(Expression leftExpression, Token operatorToken, Expression rightExpression)
    {
        LeftExpression = leftExpression;
        OperatorToken = operatorToken;
        RightExpression = rightExpression;
    }
}