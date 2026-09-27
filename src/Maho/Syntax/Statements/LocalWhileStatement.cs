namespace Maho.Syntax;

/// <summary> Local while statement with an optional declaration, loop condition, and single body statement. </summary>
internal sealed class LocalWhileStatement : LocalStatement
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
    public LocalStatement Body { get; }

    /// <summary> Creates one local while statement node with an optional declaration. </summary>
    public LocalWhileStatement(Token keyword, Token openParen, VariableDeclaration? declaration, Token? semicolon, Expression condition, Token closeParen, LocalStatement body)
    {
        Keyword = keyword;
        OpenParen = openParen;
        Declaration = declaration;
        Semicolon = semicolon;
        Condition = condition;
        CloseParen = closeParen;
        Body = body;
    }

    /// <summary> Creates one local while statement node with condition only. </summary>
    public LocalWhileStatement(Token keyword, Token openParen, Expression condition, Token closeParen, LocalStatement body)
        : this(keyword, openParen, null, null, condition, closeParen, body)
    {
    }
}
