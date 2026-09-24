namespace Maho.Syntax;

/// <summary> Function or method invocation expression. </summary>
internal sealed class CallExpression : Expression
{
    /// <summary> Expression being invoked. </summary>
    public Expression Callee { get; internal set; }
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }
    /// <summary> Argument list. </summary>
    public SeparatedSyntaxList<Expression> Arguments { get; internal set; }
    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }

    /// <summary> Creates one call expression node. </summary>
    public CallExpression(Expression callee, Token openParen, SeparatedSyntaxList<Expression> arguments, Token closeParen)
    {
        Callee = callee;
        OpenParen = openParen;
        Arguments = arguments;
        CloseParen = closeParen;
    }
}