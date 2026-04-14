namespace Maho.Syntax;

/// <summary> One bracketed list of attribute applications attached to a declaration. </summary>
internal sealed class AttributeListSyntax : SyntaxNode
{
    /// <summary> Opening bracket token. </summary>
    public Token OpenBracket { get; }
    /// <summary> Applied attributes in source order. </summary>
    public SeparatedSyntaxList<AttributeApplication> Attributes { get; }
    /// <summary> Closing bracket token. </summary>
    public Token CloseBracket { get; }

    /// <summary> Creates one parsed attribute list. </summary>
    public AttributeListSyntax(Token openBracket, SeparatedSyntaxList<AttributeApplication> attributes, Token closeBracket)
    {
        OpenBracket = openBracket;
        Attributes = attributes;
        CloseBracket = closeBracket;
    }
}
