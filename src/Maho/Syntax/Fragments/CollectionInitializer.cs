namespace Maho.Syntax;

internal sealed class CollectionInitializer : SyntaxNode
{
    public Token LeftBrace { get; }
    public SeparatedSyntaxList<Expression> Expressions { get; }
    public Token RightBrace { get; }

    public CollectionInitializer(Token leftBrace, SeparatedSyntaxList<Expression> expressions, Token rightBrace)
    {
        LeftBrace = leftBrace;
        Expressions = expressions;
        RightBrace = rightBrace;
    }
}