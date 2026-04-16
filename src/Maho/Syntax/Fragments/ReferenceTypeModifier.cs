namespace Maho.Syntax;

/// <summary> Reference postfix type modifier. </summary>
internal sealed class ReferenceTypeModifier : PostfixTypeModifier
{
    /// <summary> Ampersand token. </summary>
    public Token Ampersand { get; }
    /// <summary> Modifier kind value. </summary>
    public static PostfixTypeModifierKind Kind => PostfixTypeModifierKind.Reference;

    /// <summary> Creates one reference modifier node. </summary>
    public ReferenceTypeModifier(Token ampersand)
    {
        Ampersand = ampersand;
    }
}
