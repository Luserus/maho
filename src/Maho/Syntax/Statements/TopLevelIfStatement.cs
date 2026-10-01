namespace Maho.Syntax;

/// <summary> Top-level if statement with an optional declaration, condition, and optional else branch. </summary>
internal sealed class TopLevelIfStatement : TopLevelStatement
{
    /// <summary> The if keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }
    /// <summary> Optional declaration in the header, e.g. <c>int x = foo()</c>. </summary>
    public VariableDeclaration? Declaration { get; }
    /// <summary> Optional semicolon token separating the declaration from the condition. </summary>
    public Token? Semicolon { get; }
    /// <summary> Condition expression. </summary>
    public Expression Condition { get; }
    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }
    /// <summary> Then-branch statement. </summary>
    public TopLevelStatement ThenStatement { get; }
    /// <summary> Optional else branch. </summary>
    public TopLevelElseStatement? ElseStatement { get; }

    /// <summary> Creates one top-level if statement node with an optional declaration. </summary>
    public TopLevelIfStatement(Token keyword, Token openParen, VariableDeclaration? declaration, Token? semicolon, Expression condition, Token closeParen, TopLevelStatement thenStatement, TopLevelElseStatement? elseStatement)
    {
        Keyword = keyword;
        OpenParen = openParen;
        Declaration = declaration;
        Semicolon = semicolon;
        Condition = condition;
        CloseParen = closeParen;
        ThenStatement = thenStatement;
        ElseStatement = elseStatement;
    }

    /// <summary> Creates one top-level if statement node with condition only. </summary>
    public TopLevelIfStatement(Token keyword, Token openParen, Expression condition, Token closeParen, TopLevelStatement thenStatement, TopLevelElseStatement? elseStatement)
        : this(keyword, openParen, null, null, condition, closeParen, thenStatement, elseStatement)
    {
    }
}
