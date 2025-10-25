namespace Maho.Syntax;

/// <summary> Represents an identifier expression node. </summary>
internal sealed class IdentifierExpression : NamedExpression
{
    public IdentifierExpression(IdentifierName identifier) : base(identifier)
    { }
}