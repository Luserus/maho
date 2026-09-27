namespace Maho.Syntax;

/// <summary> Tuple type syntax consisting of a comma-separated list of element types enclosed in parentheses. </summary>
internal sealed class TupleType : TypeSyntax
{
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }
    /// <summary> Element types in the tuple. </summary>
    public SeparatedSyntaxList<TypeSyntax> Elements { get; }
    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }

    /// <summary> Creates one tuple type node. </summary>
    public TupleType(Token openParen, SeparatedSyntaxList<TypeSyntax> elements, Token closeParen)
    {
        OpenParen = openParen;
        Elements = elements;
        CloseParen = closeParen;
    }
}
