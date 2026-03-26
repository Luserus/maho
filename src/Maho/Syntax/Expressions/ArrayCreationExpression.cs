namespace Maho.Syntax;

internal sealed class ArrayCreationExpression : ObjectCreationExpression
{
    public TypeSyntax Type { get; }
    public Token LeftBracket { get; }
    public Expression? Size { get; }
    public Token RightBracket { get; }
    public CollectionExpression? Initializer { get; }

    public ArrayCreationExpression(Token keyword, ObjectCreationKind kind, TypeSyntax type, Token leftBracket, Expression? size, Token rightBracket, CollectionExpression? initializer) : base(keyword, kind)
    {
        Type = type;
        LeftBracket = leftBracket;
        Size = size;
        RightBracket = rightBracket;
        Initializer = initializer;
    }
}