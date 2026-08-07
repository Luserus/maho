namespace Maho.Syntax;

/// <summary> Named argument expression inside a callable argument list. </summary>
internal sealed class NamedArgumentExpression : Expression
{
    /// <summary> Argument name token. </summary>
    public Token Name { get; }
    /// <summary> Colon token after the argument name. </summary>
    public Token Colon { get; }
    /// <summary> Argument value expression. </summary>
    public Expression Value { get; }

    /// <summary> Creates one named argument expression. </summary>
    public NamedArgumentExpression(Token name, Token colon, Expression value)
    {
        Name = name;
        Colon = colon;
        Value = value;
    }
}
