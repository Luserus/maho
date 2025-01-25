namespace Maho.Syntax;

/// <summary> Represents a unary expression node. </summary>
internal sealed class UnaryExpressionSyntax : ExpressionSyntax
{
    /// <summary> Represents a unary expression node. </summary>
    public Token OperatorToken {get; }
    /// <summary> The expression on which the unary operator acts on. </summary>
    public ExpressionSyntax Operand { get; }

    /// <summary> Initializes the UnaryExpressionSyntax class. </summary>
    /// <param name="operatorToken"> The unary operator. </param>
    /// <param name="operand"> The expression on which the unary operator acts on. </param>
    public UnaryExpressionSyntax(Token operatorToken, ExpressionSyntax operand)
    {
        OperatorToken = operatorToken;
        Operand = operand;
    }
}