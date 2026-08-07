namespace Maho.Syntax;

/// <summary> Pointer postfix type modifier. </summary>
internal sealed class PointerTypeModifier : PostfixTypeModifier
{
    /// <summary> Asterisk token. </summary>
    public Token Asterisk { get; }

    /// <summary> Creates one pointer modifier node. </summary>
    public PointerTypeModifier(Token asterisk) : base(PostfixTypeModifierKind.Pointer) => Asterisk = asterisk;
}
