namespace Maho.Syntax;

/// <summary> Represents a nameof(...) expression node. </summary>
internal sealed class NameofExpression : Expression
{
    public Token Keyword { get; }
    public Token OpenParen { get; }
    public Expression Argument { get; set; }
    public Token CloseParen { get; }

    public NameofExpression(Token keyword, Token openParen, Expression argument, Token closeParen)
    {
        Keyword = keyword;
        OpenParen = openParen;
        Argument = argument;
        CloseParen = closeParen;
    }
}
