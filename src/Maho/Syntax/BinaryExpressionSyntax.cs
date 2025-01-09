namespace Maho.Syntax;

/// <summary> Represents a binary expression node. </summary>
/// <param name="leftExpression"> The Left-Hand-Side expression. </param>
/// <param name="operatorToken"> The binary operator. </param>
/// <param name="rightExpression"> The Right-Hand-Side expression. </param>
internal sealed class BinaryExpressionSyntax(ExpressionSyntax leftExpression, Token operatorToken, ExpressionSyntax rightExpression) : ExpressionSyntax
{
    /// <summary> The Left-Hand-Side expression. </summary>
    public ExpressionSyntax LeftExpression { get; } = leftExpression;
    /// <summary> The binary operator. </summary>
    public Token OperatorToken { get; }= operatorToken;
    /// <summary> The Right-Hand-Side expression. </summary>
    public ExpressionSyntax RightExpression { get; } = rightExpression;
}