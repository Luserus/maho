namespace Maho.Syntax;

/// <summary> One attribute application inside an attribute list. </summary>
internal sealed class AttributeApplication : SyntaxNode
{
    /// <summary> Attribute type name being applied, including any qualified parts. </summary>
    public NamedSyntax Name { get; }
    /// <summary> Opening parenthesis token for constructor arguments, when present. </summary>
    public Token? OpenParen { get; }
    /// <summary> Constructor arguments passed to the attribute application. </summary>
    public SeparatedSyntaxList<Expression> Arguments { get; }
    /// <summary> Closing parenthesis token for constructor arguments, when present. </summary>
    public Token? CloseParen { get; }

    /// <summary> Creates one attribute application from its parsed name and optional constructor arguments. </summary>
    public AttributeApplication(NamedSyntax name, Token? openParen, SeparatedSyntaxList<Expression> arguments, Token? closeParen)
    {
        Name = name;
        OpenParen = openParen;
        Arguments = arguments;
        CloseParen = closeParen;
    }
}
