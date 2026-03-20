namespace Maho.Syntax;

internal sealed class MemberAccessExpression : Expression
{
    public Expression Expression { get; }
    public Token Dot { get; }
    public Token Identifier { get; }

    public MemberAccessExpression(Expression expression, Token dot, Token identifier)
    {
        Expression = expression;
        Dot = dot;
        Identifier = identifier;
    }
}