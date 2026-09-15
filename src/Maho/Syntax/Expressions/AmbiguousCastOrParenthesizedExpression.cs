namespace Maho.Syntax;

/// <summary> Expression that may be either a cast expression or a parenthesized expression with continuations. </summary>
internal sealed class AmbiguousCastOrParenthesizedExpression : Expression
{
    /// <summary> Cast-shaped interpretation. </summary>
    public CastExpression CastExpression { get; }
    /// <summary> Parenthesized-expression-shaped interpretation. </summary>
    public Expression ParenthesizedExpression { get; }

    /// <summary> Creates one ambiguous cast-or-parenthesized expression node. </summary>
    public AmbiguousCastOrParenthesizedExpression(CastExpression castExpression, Expression parenthesizedExpression)
    {
        CastExpression = castExpression;
        ParenthesizedExpression = parenthesizedExpression;
    }
}
