namespace Maho.Syntax;

/// <summary>A literal expression supplied as a compile-time generic argument.</summary>
internal sealed class LiteralTypeArgument : TypeSyntax
{
    /// <summary>The literal expression representing the compile-time value.</summary>
    public LiteralExpression Expression { get; }

    /// <summary>The token of <see cref="Expression"/>.</summary>
    public Token Literal => Expression.Literal;

    /// <summary>Creates one literal-expression generic argument.</summary>
    public LiteralTypeArgument(LiteralExpression expression) => Expression = expression;
}
