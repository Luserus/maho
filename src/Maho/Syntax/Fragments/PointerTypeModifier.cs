namespace Maho.Syntax;

/// <summary> Pointer postfix type modifier (<c>T*</c> or <c>T mut*</c>). </summary>
internal sealed class PointerTypeModifier : PostfixTypeModifier
{
    /// <summary> Asterisk token. </summary>
    public Token Asterisk { get; }

    /// <summary> Optional <c>mut</c> token for mutable pointers (<c>T mut*</c>). </summary>
    public Token? MutToken { get; }

    /// <summary> Gets whether this is a mutable pointer (<c>T mut*</c>). </summary>
    public bool IsMutable => MutToken is not null;

    /// <summary> Creates one pointer modifier node. </summary>
    public PointerTypeModifier(Token asterisk, Token? mutToken = null) : base(PostfixTypeModifierKind.Pointer)
    {
        Asterisk = asterisk;
        MutToken = mutToken;
    }
}