namespace Maho.Syntax;

/// <summary> Represents an expression that is also a statement. </summary>
/// <param name="expression"> The expression. </param>
/// <param name="semicolon"> The semicolon to mark the end of the statement. </param>
internal sealed class ExpressionStatementSyntax(ExpressionSyntax expression, Token semicolon) : StatementSyntax
{
    /// <summary> The expression. </summary>
    public ExpressionSyntax Expression { get; } = expression;
    /// <summary> The semicolon to mark the end of the statement. </summary>
    public Token Semicolon { get; } = semicolon;
}