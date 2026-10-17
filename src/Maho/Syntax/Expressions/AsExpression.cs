namespace Maho.Syntax;

/// <summary> Explicit type cast expression using the 'as' keyword, e.g. <c>expr as Type</c>. </summary>
internal sealed class AsExpression : Expression
{
    /// <summary> Expression being cast. </summary>
    public Expression Expression { get; }
    /// <summary> The 'as' contextual keyword token. </summary>
    public Token AsKeyword { get; }
    /// <summary> Target type syntax. </summary>
    public TypeSyntax Type { get; }

    /// <summary> Creates one 'as' cast expression node. </summary>
    public AsExpression(Expression expression, Token asKeyword, TypeSyntax type)
    {
        Expression = expression;
        AsKeyword = asKeyword;
        Type = type;
    }
}
