namespace Maho.Syntax;

internal sealed class CollectionExpression : Expression
{
    public Token LeftBracket { get; }
    public SeparatedSyntaxList<Expression> Expressions { get; }
    public Token RightBracket { get; }

    public CollectionExpression(Token leftBracket, SeparatedSyntaxList<Expression> expressions, Token rightBracket)
    {
        LeftBracket = leftBracket;
        Expressions = expressions;
        RightBracket = rightBracket;
    }
}