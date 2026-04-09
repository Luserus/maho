namespace Maho.Syntax;

/// <summary> Top-level while statement with a single body statement. </summary>
internal sealed class TopLevelWhileStatement : TopLevelStatement
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
    public TopLevelStatement Statement { get; }

    /// <summary> Creates one top-level while statement node. </summary>
    public TopLevelWhileStatement(Token keyword, Token openParen, Expression condition, Token closeParen, TopLevelStatement statement)
    {
        Keyword = keyword;
        OpenParen = openParen;
        Condition = condition;
        CloseParen = closeParen;
        Statement = statement;
    }
}
