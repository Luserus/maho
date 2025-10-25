namespace Maho.Syntax;

/// <summary> Represents an expression that is also a statement. </summary>
internal sealed class ExpressionStatement : Statement
{
    /// <summary> The expression. </summary>
    public Expression Expression { get; }
    /// <summary> The semicolon to mark the end of the statement. </summary>
    public Token Semicolon { get; }
    public bool IsFinalExpression { get; }

    /// <param name="expression"> The expression. </param>
    /// <param name="semicolon"> The semicolon to mark the end of the statement. </param>
    public ExpressionStatement(Expression expression, Token semicolon, bool isFinalExpression = false)
    {
        Expression = expression;
        Semicolon = semicolon;
        IsFinalExpression = isFinalExpression;
    }
}