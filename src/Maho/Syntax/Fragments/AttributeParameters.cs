namespace Maho.Syntax;

internal sealed class AttributeParameters : SyntaxNode
{
    public Token OpenParen { get; }
    public SeparatedSyntaxList<Parameter> Parameters { get; }
    public Token CloseParen { get; }

    public AttributeParameters(Token openParen, SeparatedSyntaxList<Parameter> parameters, Token closeParen)
    {
        OpenParen = openParen;
        Parameters = parameters;
        CloseParen = closeParen;
    }
}