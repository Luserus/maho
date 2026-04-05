namespace Maho.Syntax;

/// <summary> Array postfix type modifier. </summary>
internal sealed class ArrayTypeModifier : PostfixTypeModifier
{
    /// <summary> Opening bracket token. </summary>
    public Token LeftBracket { get; }
    /// <summary> Optional array size expression. </summary>
    public Expression? Size { get; }
    /// <summary> Closing bracket token. </summary>
    public Token RightBracket { get; }
    /// <summary> Modifier kind value. </summary>
    public PostfixTypeModifierKind Kind => PostfixTypeModifierKind.Array;

    /// <summary> Creates one array modifier node. </summary>
    public ArrayTypeModifier(Token leftBracket, Expression? size, Token rightBracket)
    {
        LeftBracket = leftBracket;
        Size =  size;
        RightBracket = rightBracket;
    }
}
