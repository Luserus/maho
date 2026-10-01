namespace Maho.Syntax;

/// <summary> Top-level while statement with an optional declaration, loop condition, and single body statement. </summary>
internal sealed class TopLevelWhileStatement : TopLevelStatement
{
    /// <summary> The while keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }
    /// <summary> Optional declaration in the header, e.g. <c>int x = foo()</c>. </summary>
    public VariableDeclaration? Declaration { get; }
    /// <summary> Optional semicolon token separating the declaration from the condition. </summary>
    public Token? Semicolon { get; }
    /// <summary> Loop condition expression. </summary>
    public Expression Condition { get; }
    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }
    /// <summary> Loop body statement. </summary>
    public TopLevelStatement Statement { get; }

    /// <summary> Creates one top-level while statement node with an optional declaration. </summary>
    public TopLevelWhileStatement(Token keyword, Token openParen, VariableDeclaration? declaration, Token? semicolon, Expression condition, Token closeParen, TopLevelStatement statement)
    {
        Keyword = keyword;
        OpenParen = openParen;
        Declaration = declaration;
        Semicolon = semicolon;
        Condition = condition;
        CloseParen = closeParen;
        Statement = statement;
    }

    /// <summary> Creates one top-level while statement node with condition only. </summary>
    public TopLevelWhileStatement(Token keyword, Token openParen, Expression condition, Token closeParen, TopLevelStatement statement)
        : this(keyword, openParen, null, null, condition, closeParen, statement)
    {
    }
}
