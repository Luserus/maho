namespace Maho.Syntax;

/// <summary> Constructor-style <c>with</c> modifier for collection expressions. </summary>
internal sealed class CollectionConstructorModifier : CollectionExpressionModifier
{
    /// <summary> With keyword token. </summary>
    public Token WithKeyword { get; }
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }
    /// <summary> Constructor arguments. </summary>
    public SeparatedSyntaxList<Expression> Arguments { get; }
    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }

    /// <summary> Creates one collection constructor modifier. </summary>
    public CollectionConstructorModifier(Token withKeyword, Token openParen, SeparatedSyntaxList<Expression> arguments, Token closeParen)
    {
        WithKeyword = withKeyword;
        OpenParen = openParen;
        Arguments = arguments;
        CloseParen = closeParen;
    }
}
