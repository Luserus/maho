namespace Maho.Syntax;

/// <summary> Expression-form else branch that attaches to an enclosing if expression. </summary>
internal sealed class ElseExpression : Expression
{
    /// <summary> The else keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Nested expression guarded by the else branch. </summary>
    public Expression Expression { get; }

    /// <summary> Creates one else-expression node. </summary>
    public ElseExpression(Token keyword, Expression expression)
    {
        Keyword = keyword;
        Expression = expression;
    }
}
