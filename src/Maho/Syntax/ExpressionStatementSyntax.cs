namespace Maho.Syntax;

/// <summary> Represents an expression that is also a statement. </summary>
internal sealed class ExpressionStatementSyntax : StatementSyntax
{
    /// <summary> The expression. </summary>
    public ExpressionSyntax Expression { get; }
    /// <summary> The semicolon to mark the end of the statement. </summary>
    public Token Semicolon { get; }

    /// <param name="expression"> The expression. </param>
    /// <param name="semicolon"> The semicolon to mark the end of the statement. </param>
    public ExpressionStatementSyntax(ExpressionSyntax expression, Token semicolon)
    {
        Expression = expression;
        Semicolon = semicolon;
    }
}