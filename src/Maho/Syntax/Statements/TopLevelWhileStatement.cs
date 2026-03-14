namespace Maho.Syntax;

internal sealed class TopLevelWhileStatement : TopLevelStatement
{
    public Token Keyword { get; }
    public Token OpenParen { get; }
    public Expression Condition { get; }
    public Token CloseParen { get; }
    public TopLevelStatement Statement { get; }

    public TopLevelWhileStatement(Token keyword, Token openParen, Expression condition, Token closeParen, TopLevelStatement statement)
    {
        Keyword = keyword;
        OpenParen = openParen;
        Condition = condition;
        CloseParen = closeParen;
        Statement = statement;
    }
}