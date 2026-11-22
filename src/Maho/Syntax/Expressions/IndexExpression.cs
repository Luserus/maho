namespace Maho.Syntax;

/// <summary> Indexing expression that reads from a subscripting operation. </summary>
internal sealed class IndexExpression : Expression
{
    /// <summary> Expression being indexed. </summary>
    public Expression Expression { get; }
    /// <summary> Opening bracket token. </summary>
    public Token OpenBracketToken { get; }
    /// <summary> Index argument expressions. </summary>
    public SeparatedSyntaxList<Expression> Arguments { get; }
    /// <summary> Closing bracket token. </summary>
    public Token CloseBracketToken { get; }

    /// <summary> Gets the first index expression (for backward compatibility). </summary>
    public Expression Index => Arguments.Count > 0 ? Arguments[0] : null!;

    /// <summary> Creates one multi-argument index expression node. </summary>
    public IndexExpression(Expression expression, Token openBracketToken, SeparatedSyntaxList<Expression> arguments, Token closeBracketToken)
    {
        Expression = expression;
        OpenBracketToken = openBracketToken;
        Arguments = arguments;
        CloseBracketToken = closeBracketToken;
    }

    /// <summary> Creates one single-argument index expression node. </summary>
    public IndexExpression(Expression expression, Token openBracketToken, Expression index, Token closeBracketToken)
        : this(expression, openBracketToken, new SeparatedSyntaxList<Expression>([index]), closeBracketToken)
    {
    }
}