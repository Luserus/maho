namespace Maho.Syntax;

/// <summary> Member access expression using dot notation. </summary>
internal sealed class MemberAccessExpression : Expression
{
    /// <summary> Target expression being accessed. </summary>
    public Expression Expression { get; }
    /// <summary> Dot token. </summary>
    public Token Dot { get; }
    /// <summary> Member identifier token. </summary>
    public Token Identifier { get; }

    /// <summary> Creates one member-access expression node. </summary>
    public MemberAccessExpression(Expression expression, Token dot, Token identifier)
    {
        Expression = expression;
        Dot = dot;
        Identifier = identifier;
    }
}
