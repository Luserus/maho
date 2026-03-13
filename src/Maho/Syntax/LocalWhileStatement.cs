namespace Maho.Syntax;

internal sealed class LocalWhileStatement : LocalStatement
{
    public Token Keyword { get; }
    public Token OpenParen { get; }
    public Expression Condition { get; }
    public Token CloseParen { get; }
    public LocalStatement Body { get; }

    public LocalWhileStatement(Token keyword, Token openParen, Expression condition, Token closeParen, LocalStatement body)
    {
        Keyword = keyword;
        OpenParen = openParen;
        Condition = condition;
        CloseParen = closeParen;
        Body = body;
    }
}