namespace Maho.Syntax;

internal abstract class NamedExpression : Expression
{
    public Token Identifier { get; }

    protected NamedExpression(Token identifier) => Identifier = identifier;
}