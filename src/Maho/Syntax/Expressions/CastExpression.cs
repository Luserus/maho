namespace Maho.Syntax;

/// <summary> Cast expression with an explicit target type. </summary>
internal sealed class CastExpression : Expression
{
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }
    /// <summary> Target type syntax. </summary>
    public TypeSyntax Type { get; }
    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }
    /// <summary> Expression being cast. </summary>
    public Expression Expression { get; }

    /// <summary> Creates one cast expression node. </summary>
    public CastExpression(Token openParen, TypeSyntax type, Token closeParen, Expression expression)
    {
        OpenParen = openParen;
        Type = type;
        CloseParen = closeParen;
        Expression = expression;
    }
}
