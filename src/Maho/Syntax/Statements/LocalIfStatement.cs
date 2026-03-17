namespace Maho.Syntax;

internal sealed class LocalIfStatement : LocalStatement
{
    public Token Keyword { get; }
    public Token OpenParen { get; }
    public Expression Condition { get; }
    public Token CloseParen { get; }
    public LocalStatement ThenStatement { get; }
    public LocalElseStatement? ElseStatement { get; }

    public LocalIfStatement(Token keyword, Token openParen, Expression condition, Token closeParen, LocalStatement thenStatement, LocalElseStatement? elseStatement)
    {
        Keyword = keyword;
        OpenParen = openParen;
        Condition = condition;
        CloseParen = closeParen;
        ThenStatement = thenStatement;
        ElseStatement = elseStatement;
    }
}
