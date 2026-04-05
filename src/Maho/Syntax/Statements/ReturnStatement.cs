namespace Maho.Syntax;

/// <summary> Return statement with an optional value expression. </summary>
internal sealed class ReturnStatement : SyntaxNode
{
    /// <summary> The return keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Optional returned expression. </summary>
    public Expression? Expression { get; }
    /// <summary> The terminating semicolon token. </summary>
    public Token Semicolon { get; }

    /// <summary> Creates one return statement node. </summary>
    public ReturnStatement(Token keyword, Expression? expression, Token semicolon)
    {
        Keyword = keyword;
        Expression = expression;
        Semicolon = semicolon;
    }
}
