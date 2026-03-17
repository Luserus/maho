namespace Maho.Syntax;

internal sealed class TopLevelExpressionStatement : TopLevelStatement
{
    /// <summary> The expression. </summary>
    public Expression Expression { get; }
    /// <summary> The semicolon to mark the end of the statement. </summary>
    public Token Semicolon { get; }

    /// <param name="expression"> The expression. </param>
    /// <param name="semicolon"> The semicolon to mark the end of the statement. </param>
    public TopLevelExpressionStatement(Expression expression, Token semicolon)
    {
        Expression = expression;
        Semicolon = semicolon;
    }
}