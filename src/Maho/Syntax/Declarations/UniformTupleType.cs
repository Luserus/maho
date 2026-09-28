namespace Maho.Syntax;

/// <summary> Tuple type syntax with a uniform element type and explicit element names, e.g. <c>Type (a, b, c)</c>. </summary>
internal sealed class UniformTupleType : TypeSyntax
{
    /// <summary> The uniform type shared by all elements in the tuple. </summary>
    public TypeSyntax ElementType { get; }

    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }

    /// <summary> Element names in the tuple. </summary>
    public SeparatedSyntaxList<SimpleName> Elements { get; }

    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }

    public UniformTupleType(TypeSyntax elementType, Token openParen, SeparatedSyntaxList<SimpleName> elements, Token closeParen)
    {
        ElementType = elementType;
        OpenParen = openParen;
        Elements = elements;
        CloseParen = closeParen;
    }
}
