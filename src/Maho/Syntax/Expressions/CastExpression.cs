namespace Maho.Syntax;

internal sealed class CastExpression : Expression
{
    public Token OpenParen { get; }
    public TypeSyntax Type { get; }
    public Token CloseParen { get; }
    public Expression Expression { get; }

    public CastExpression(Token openParen, TypeSyntax type, Token closeParen, Expression expression)
    {
        OpenParen = openParen;
        Type = type;
        CloseParen = closeParen;
        Expression = expression;
    }
}