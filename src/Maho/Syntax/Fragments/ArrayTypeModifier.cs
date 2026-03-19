namespace Maho.Syntax;

internal sealed class ArrayTypeModifier : PostfixTypeModifier
{
    public Token OpenBracket { get; }
    public Expression? Size { get; }
    public Token CloseBracket { get; }
    public PostfixTypeModifierKind Kind => PostfixTypeModifierKind.Array;

    public ArrayTypeModifier(Token openBracket, Expression? size, Token closeBracket)
    {
        OpenBracket = openBracket;
        Size =  size;
        CloseBracket = closeBracket;
    }
}
