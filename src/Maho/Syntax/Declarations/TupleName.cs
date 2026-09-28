namespace Maho.Syntax;

/// <summary> Tuple of names used as a declarator in tuple variable declarations, e.g. <c>Type (a, b, c);</c>. </summary>
internal sealed class TupleName : NamedSyntax
{
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }

    /// <summary> Comma-separated names in the tuple. </summary>
    public SeparatedSyntaxList<NamedSyntax> Elements { get; }

    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }

    public TupleName(Token openParen, SeparatedSyntaxList<NamedSyntax> elements, Token closeParen)
    {
        OpenParen = openParen;
        Elements = elements;
        CloseParen = closeParen;
    }
}
