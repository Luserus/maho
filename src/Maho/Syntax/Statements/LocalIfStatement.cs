namespace Maho.Syntax;

/// <summary> Local if statement with an optional else branch. </summary>
internal sealed class LocalIfStatement : LocalStatement
{
    /// <summary> The if keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }
    /// <summary> Condition expression. </summary>
    public Expression Condition { get; }
    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }
    /// <summary> Then-branch statement. </summary>
    public LocalStatement ThenStatement { get; }
    /// <summary> Optional else branch. </summary>
    public LocalElseStatement? ElseStatement { get; }

    /// <summary> Creates one local if statement node. </summary>
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
