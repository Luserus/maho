namespace Maho.Syntax;

/// <summary> Pointer postfix type modifier. </summary>
internal sealed class PointerTypeModifier : PostfixTypeModifier
{
    /// <summary> Asterisk token. </summary>
    public Token Asterisk { get; }
    /// <summary> Modifier kind value. </summary>
    public static PostfixTypeModifierKind Kind => PostfixTypeModifierKind.Pointer;

    /// <summary> Creates one pointer modifier node. </summary>
    public PointerTypeModifier(Token asterisk) => Asterisk = asterisk;
}
