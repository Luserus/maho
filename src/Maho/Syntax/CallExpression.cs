namespace Maho.Syntax;

internal sealed class CallExpression : Expression
{
    public Expression Callee { get; }
    public Token OpenParen { get; }
    public ISeparatedSyntaxList Arguments { get; }
    public Token CloseParen { get; }

    public CallExpression(Expression callee, Token openParen, ISeparatedSyntaxList arguments, Token closeParen)
    {
        Callee = callee;
        OpenParen = openParen;
        Arguments = arguments;
        CloseParen = closeParen;
    }
}