namespace Maho.Syntax;

/// <summary> For-in loop expression iterating over a collection or iterable. </summary>
internal sealed class ForExpression : Expression
{
    /// <summary> The for keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }
    /// <summary> Optional variable declaration if declaring a new variable (e.g. <c>var i</c>). </summary>
    public VariableDeclaration? Declaration { get; }
    /// <summary> Variable expression if reusing an existing variable (e.g. <c>i</c>). </summary>
    public Expression? VariableExpression { get; }
    /// <summary> The 'in' keyword token. </summary>
    public Token InKeyword { get; }
    /// <summary> The collection expression being iterated. </summary>
    public Expression Collection { get; }
    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }
    /// <summary> Loop body expression. </summary>
    public Expression Body { get; }

    public ForExpression(
        Token keyword,
        Token openParen,
        VariableDeclaration? declaration,
        Expression? variableExpression,
        Token inKeyword,
        Expression collection,
        Token closeParen,
        Expression body)
    {
        Keyword = keyword;
        OpenParen = openParen;
        Declaration = declaration;
        VariableExpression = variableExpression;
        InKeyword = inKeyword;
        Collection = collection;
        CloseParen = closeParen;
        Body = body;
    }
}
