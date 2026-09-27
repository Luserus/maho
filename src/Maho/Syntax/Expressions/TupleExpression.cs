namespace Maho.Syntax;

/// <summary> Tuple expression syntax consisting of a comma-separated list of element expressions enclosed in parentheses. </summary>
internal sealed class TupleExpression : Expression
{
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }
    /// <summary> Element expressions in the tuple. </summary>
    public SeparatedSyntaxList<Expression> Arguments { get; }
    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }

    /// <summary> Creates one tuple expression node. </summary>
    public TupleExpression(Token openParen, SeparatedSyntaxList<Expression> arguments, Token closeParen)
    {
        OpenParen = openParen;
        Arguments = arguments;
        CloseParen = closeParen;
    }
}
