namespace Maho.Syntax;

/// <summary> Collection initializer enclosed in braces. </summary>
internal sealed class CollectionInitializer : SyntaxNode
{
    /// <summary> Opening brace token. </summary>
    public Token LeftBrace { get; }
    /// <summary> Initializer expressions in source order. </summary>
    public SeparatedSyntaxList<Expression> Expressions { get; }
    /// <summary> Closing brace token. </summary>
    public Token RightBrace { get; }

    /// <summary> Creates one collection initializer node. </summary>
    public CollectionInitializer(Token leftBrace, SeparatedSyntaxList<Expression> expressions, Token rightBrace)
    {
        LeftBrace = leftBrace;
        Expressions = expressions;
        RightBrace = rightBrace;
    }
}
