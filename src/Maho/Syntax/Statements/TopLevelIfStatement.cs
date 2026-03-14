namespace Maho.Syntax;

internal sealed class TopLevelIfStatement : TopLevelStatement
{
    public Token Keyword { get; }
    public Token OpenParen { get; }
    public Expression Condition { get; }
    public Token CloseParen { get; }
    public TopLevelStatement ThenStatement { get; }
    public TopLevelElseStatement? ElseStatement { get; }

    public TopLevelIfStatement(Token keyword, Token openParen, Expression condition, Token closeParen, TopLevelStatement thenStatement, TopLevelElseStatement? elseStatement)
    {
        Keyword = keyword;
        OpenParen = openParen;
        Condition = condition;
        CloseParen = closeParen;
        ThenStatement = thenStatement;
        ElseStatement = elseStatement;
    }
}