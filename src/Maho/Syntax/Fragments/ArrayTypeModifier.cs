namespace Maho.Syntax;

internal sealed class ArrayTypeModifier : PostfixTypeModifier
{
    public Token OpenBracket { get; }
    public Token CloseBracket { get; }
    public PostfixTypeModifierKind Kind => PostfixTypeModifierKind.Array;

    public ArrayTypeModifier(Token openBracket, Token closeBracket)
    {
        OpenBracket = openBracket;
        CloseBracket = closeBracket;
    }
}
