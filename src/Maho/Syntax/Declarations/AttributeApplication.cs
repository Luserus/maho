namespace Maho.Syntax;

/// <summary> One attribute application inside an attribute list. </summary>
internal sealed class AttributeApplication : SyntaxNode
{
    /// <summary> Optional dollar token indicating a macro attribute application (e.g. [$Name]). </summary>
    public Token? DollarToken { get; }
    /// <summary> Indicates whether this application invokes a macro attribute. </summary>
    public bool IsMacroAttribute => DollarToken is not null;
    /// <summary> Attribute type name being applied, including any qualified parts. </summary>
    public NamedSyntax Name { get; }
    /// <summary> Opening parenthesis token for constructor arguments, when present. </summary>
    public Token? OpenParen { get; }
    /// <summary> Constructor arguments passed to the attribute application. </summary>
    public SeparatedSyntaxList<Expression> Arguments { get; }
    /// <summary> Closing parenthesis token for constructor arguments, when present. </summary>
    public Token? CloseParen { get; }

    /// <summary> Creates one attribute application from its parsed components. </summary>
    public AttributeApplication(Token? dollarToken, NamedSyntax name, Token? openParen, SeparatedSyntaxList<Expression> arguments, Token? closeParen)
    {
        DollarToken = dollarToken;
        Name = name;
        OpenParen = openParen;
        Arguments = arguments;
        CloseParen = closeParen;
    }

    /// <summary> Creates one attribute application from its parsed name and optional constructor arguments. </summary>
    public AttributeApplication(NamedSyntax name, Token? openParen, SeparatedSyntaxList<Expression> arguments, Token? closeParen)
        : this(null, name, openParen, arguments, closeParen) { }
}