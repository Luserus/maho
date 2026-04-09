namespace Maho.Syntax;

/// <summary> Top-level if statement with an optional else branch. </summary>
internal sealed class TopLevelIfStatement : TopLevelStatement
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
    public TopLevelStatement ThenStatement { get; }
    /// <summary> Optional else branch. </summary>
    public TopLevelElseStatement? ElseStatement { get; }

    /// <summary> Creates one top-level if statement node. </summary>
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
