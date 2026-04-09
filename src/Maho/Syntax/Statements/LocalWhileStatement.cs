namespace Maho.Syntax;

/// <summary> Local while statement with a single body statement. </summary>
internal sealed class LocalWhileStatement : LocalStatement
{
    /// <summary> The while keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }
    /// <summary> Loop condition expression. </summary>
    public Expression Condition { get; }
    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }
    /// <summary> Loop body statement. </summary>
    public LocalStatement Body { get; }

    /// <summary> Creates one local while statement node. </summary>
    public LocalWhileStatement(Token keyword, Token openParen, Expression condition, Token closeParen, LocalStatement body)
    {
        Keyword = keyword;
        OpenParen = openParen;
        Condition = condition;
        CloseParen = closeParen;
        Body = body;
    }
}
