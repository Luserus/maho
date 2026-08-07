namespace Maho.Syntax;

/// <summary> Reference postfix type modifier. </summary>
internal sealed class ReferenceTypeModifier : PostfixTypeModifier
{
    /// <summary> Ampersand token. </summary>
    public Token Ampersand { get; }

    /// <summary> Creates one reference modifier node. </summary>
    public ReferenceTypeModifier(Token ampersand) : base(PostfixTypeModifierKind.Reference)
    {
        Ampersand = ampersand;
    }
}
