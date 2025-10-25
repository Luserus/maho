namespace Maho.Syntax;

/// <summary> Represents a unary expression node. </summary>
internal sealed class UnaryExpression : Expression
{
    /// <summary> Represents a unary expression node. </summary>
    public Token OperatorToken { get; }
    /// <summary> The expression on which the unary operator acts on. </summary>
    public Expression Operand { get; }
    public UnaryPosition Position { get; }

    /// <summary> Initializes the UnaryExpressionSyntax class. </summary>
    /// <param name="operatorToken"> The unary operator. </param>
    /// <param name="operand"> The expression on which the unary operator acts on. </param>
    public UnaryExpression(Token operatorToken, Expression operand, UnaryPosition position)
    {
        OperatorToken = operatorToken;
        Operand = operand;
        Position = position;
    }
}