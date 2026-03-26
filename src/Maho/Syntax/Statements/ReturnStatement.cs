namespace Maho.Syntax;

internal sealed class ReturnStatement : SyntaxNode
{
    public Token Keyword { get; }
    public Expression? Expression { get; }
    public Token Semicolon { get; }

    public ReturnStatement(Token keyword, Expression? expression, Token semicolon)
    {
        Keyword = keyword;
        Expression = expression;
        Semicolon = semicolon;
    }
}