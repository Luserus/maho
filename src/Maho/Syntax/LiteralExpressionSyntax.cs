namespace Maho.Syntax;

/// <summary> Represents a literal expression node. </summary>
internal sealed class LiteralExpressionSyntax : ExpressionSyntax
{
    /// <summary> The literal token. </summary>
    public Token Literal { get; }

    /// <summary> Initializes the LiteralExpressionSyntax class. </summary>
    /// <param name="literal"> The literal token. </param>
    public LiteralExpressionSyntax(Token literal)
    {
        Literal = literal;
    }
}
