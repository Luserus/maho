namespace Maho.Syntax;

/// <summary> Type initializer enclosed in braces. </summary>
internal sealed class TypeInitializer : SyntaxNode
{
    /// <summary> Opening brace token. </summary>
    public Token LeftBrace { get; }
    /// <summary> Initializer expressions in source order. </summary>
    public SeparatedSyntaxList<Expression> Expressions { get; }
    /// <summary> Closing brace token. </summary>
    public Token RightBrace { get; }

    /// <summary> Creates one type initializer node. </summary>
    public TypeInitializer(Token leftBrace, SeparatedSyntaxList<Expression> expressions, Token rightBrace)
    {
        LeftBrace = leftBrace;
        Expressions = expressions;
        RightBrace = rightBrace;
    }
}