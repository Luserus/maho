namespace Maho.Syntax;

internal sealed class GenericNameExpression : NamedExpression
{
    public GenericNameExpression(GenericName genericIdentifier) : base(genericIdentifier)
    { }
}