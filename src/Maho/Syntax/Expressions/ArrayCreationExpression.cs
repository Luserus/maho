namespace Maho.Syntax;

/// <summary> Object-creation expression for array construction. </summary>
internal sealed class ArrayCreationExpression : ObjectCreationExpression
{
    /// <summary> Element type being created. </summary>
    public TypeSyntax Type { get; }
    /// <summary> Opening bracket token. </summary>
    public Token LeftBracket { get; }
    /// <summary> Optional size expression. </summary>
    public Expression? Size { get; }
    /// <summary> Closing bracket token. </summary>
    public Token RightBracket { get; }
    /// <summary> Optional collection initializer. </summary>
    public CollectionInitializer? Initializer { get; }

    /// <summary> Creates one array creation expression node. </summary>
    public ArrayCreationExpression(Token keyword, ObjectCreationKind kind, TypeSyntax type, Token leftBracket, Expression? size, Token rightBracket, CollectionInitializer? initializer) : base(keyword, kind)
    {
        Type = type;
        LeftBracket = leftBracket;
        Size = size;
        RightBracket = rightBracket;
        Initializer = initializer;
    }
}
