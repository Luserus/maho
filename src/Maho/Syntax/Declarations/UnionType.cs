namespace Maho.Syntax;

/// <summary> Represents an anonymous union type syntax, e.g. <c>(A | B | C)</c>. </summary>
internal sealed class UnionType : TypeSyntax
{
    public Token OpenParen { get; }
    public SeparatedSyntaxList<TypeSyntax> Variants { get; }
    public Token CloseParen { get; }

    public UnionType(Token openParen, SeparatedSyntaxList<TypeSyntax> variants, Token closeParen)
    {
        OpenParen = openParen;
        Variants = variants;
        CloseParen = closeParen;
    }
}

