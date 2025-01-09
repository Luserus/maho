namespace Maho.Syntax;

/// <summary> Represents a literal expression node. </summary>
/// <param name="literal"> The literal token. </param>
internal sealed class LiteralExpressionSyntax(Token literal) : ExpressionSyntax
{
    /// <summary> The literal token. </summary>
    public Token Literal { get; } = literal;
}
