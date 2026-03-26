namespace Maho.Syntax;

internal sealed class ArrayTypeModifier : PostfixTypeModifier
{
    public Token LeftBracket { get; }
    public Expression? Size { get; }
    public Token RightBracket { get; }
    public PostfixTypeModifierKind Kind => PostfixTypeModifierKind.Array;

    public ArrayTypeModifier(Token leftBracket, Expression? size, Token rightBracket)
    {
        LeftBracket = leftBracket;
        Size =  size;
        RightBracket = rightBracket;
    }
}