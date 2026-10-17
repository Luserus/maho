namespace Maho.Syntax;

/// <summary> While loop expression with an optional declaration, loop condition, and body expression. </summary>
internal sealed class WhileExpression : Expression
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
    /// <summary> Loop body expression. </summary>
    public Expression Body { get; }

    /// <summary> Creates one while expression node with an optional declaration. </summary>
    public WhileExpression(Token keyword, Token openParen, VariableDeclaration? declaration, Token? semicolon, Expression condition, Token closeParen, Expression body)
    {
        Keyword = keyword;
        OpenParen = openParen;
        Declaration = declaration;
        Semicolon = semicolon;
        Condition = condition;
        CloseParen = closeParen;
        Body = body;
    }

    /// <summary> Creates one while expression node with condition only. </summary>
    public WhileExpression(Token keyword, Token openParen, Expression condition, Token closeParen, Expression body)
        : this(keyword, openParen, null, null, condition, closeParen, body)
    {
    }
}
