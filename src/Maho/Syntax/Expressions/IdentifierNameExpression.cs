namespace Maho.Syntax;

/// <summary> Expression that refers to a single identifier name. </summary>
internal sealed class IdentifierNameExpression : NamedExpression
{
    /// <summary> Creates one identifier-name expression node. </summary>
    public IdentifierNameExpression(Token identifier) : base(identifier)
    { }
}
