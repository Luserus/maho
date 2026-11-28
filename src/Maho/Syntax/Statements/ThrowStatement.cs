namespace Maho.Syntax;

/// <summary> Throw statement with an optional thrown expression. </summary>
internal sealed class ThrowStatement : SyntaxNode
{
    /// <summary> The throw keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Optional thrown expression. </summary>
    public Expression? Expression { get; internal set; }
    /// <summary> The terminating semicolon token. </summary>
    public Token Semicolon { get; }

    /// <summary> Creates one throw statement node. </summary>
    public ThrowStatement(Token keyword, Expression? expression, Token semicolon)
    {
        Keyword = keyword;
        Expression = expression;
        Semicolon = semicolon;
    }
}

