namespace Maho.Syntax;

/// <summary> Represents a unary expression node. </summary>
/// <param name="operatorToken"> The unary operator. </param>
/// <param name="operand"> The expression on which the unary operator acts on. </param>
internal sealed class UnaryExpressionSyntax(Token operatorToken, ExpressionSyntax operand) : ExpressionSyntax
{
    /// <summary> Represents a unary expression node. </summary>
    public Token OperatorToken {get; } = operatorToken;
    /// <summary> The expression on which the unary operator acts on. </summary>
    public ExpressionSyntax Operand { get; } = operand;
}