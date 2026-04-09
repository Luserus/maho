namespace Maho.Syntax;

/// <summary> Base type for expression nodes that are anchored by a named token. </summary>
internal abstract class NamedExpression : Expression
{
    /// <summary> Identifier token for the named expression. </summary>
    public Token Identifier { get; }

    /// <summary> Creates one named expression node. </summary>
    protected NamedExpression(Token identifier) => Identifier = identifier;
}
