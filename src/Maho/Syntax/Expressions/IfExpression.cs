namespace Maho.Syntax;

/// <summary> Expression-form if/else construct. </summary>
internal sealed class IfExpression : Expression
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
    /// <summary> Then-branch expression. </summary>
    public Expression ThenExpression { get; }
    /// <summary> Optional else branch expression. </summary>
    public ElseExpression? ElseExpression { get; }

    /// <summary> Creates one if-expression node with an optional declaration. </summary>
    public IfExpression(Token keyword, Token openParen, VariableDeclaration? declaration, Token? semicolon, Expression condition, Token closeParen, Expression thenExpression, ElseExpression? elseExpression)
    {
        Keyword = keyword;
        OpenParen = openParen;
        Declaration = declaration;
        Semicolon = semicolon;
        Condition = condition;
        CloseParen = closeParen;
        ThenExpression = thenExpression;
        ElseExpression = elseExpression;
    }

    /// <summary> Creates one if-expression node with condition only. </summary>
    public IfExpression(Token keyword, Token openParen, Expression condition, Token closeParen, Expression thenExpression, ElseExpression? elseExpression)
        : this(keyword, openParen, null, null, condition, closeParen, thenExpression, elseExpression)
    {
    }
}