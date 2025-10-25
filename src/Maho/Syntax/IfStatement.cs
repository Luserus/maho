namespace Maho.Syntax;

internal sealed class IfStatement : Statement
{
    public Token IfKeyword { get; }
    public Token OpenParen { get; }
    public Expression Condition { get; }
    public Token CloseParen { get; }
    public Statement ThenStatement { get; }
    public ElseStatement? ElseStatement { get; }

    public IfStatement(Token ifKeyword, Token openParen, Expression condition, Token closeParen, Statement thenStatement, ElseStatement? elseStatement)
    {
        IfKeyword = ifKeyword;
        OpenParen = openParen;
        Condition = condition;
        CloseParen = closeParen;
        ThenStatement = thenStatement;
        ElseStatement = elseStatement;
    }
}
