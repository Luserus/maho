namespace Maho.Syntax;

internal abstract class NamedExpression : Expression
{
    public NamedSyntax Identifier { get; }

    protected NamedExpression(NamedSyntax identifier) => Identifier = identifier;
}