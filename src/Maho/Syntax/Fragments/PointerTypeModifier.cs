namespace Maho.Syntax;

internal sealed class PointerTypeModifier : PostfixTypeModifier
{
    public Token Asterisk { get; }
    public PostfixTypeModifierKind Kind => PostfixTypeModifierKind.Pointer;

    public PointerTypeModifier(Token asterisk) => Asterisk = asterisk;
}
