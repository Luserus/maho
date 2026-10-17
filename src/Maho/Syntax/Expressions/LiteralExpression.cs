namespace Maho.Syntax;

/// <summary> Represents a literal expression node. </summary>
internal sealed class LiteralExpression : Expression
{
    /// <summary> The literal token. </summary>
    public Token Literal { get; }

    /// <summary> Suffix attached to the literal token, or <c>null</c> if none. </summary>
    public string? Suffix => Literal.Suffix;

    /// <summary> Whether this literal expression has a suffix. </summary>
    public bool HasSuffix => Literal.HasSuffix;

    /// <summary> Value of the literal expression without the suffix. </summary>
    public string ValueWithoutSuffix => Literal.ValueWithoutSuffix;

    /// <summary> Initializes the LiteralExpressionSyntax class. </summary>
    /// <param name="literal"> The literal token. </param>
    public LiteralExpression(Token literal)
    {
        Literal = literal;
    }
}