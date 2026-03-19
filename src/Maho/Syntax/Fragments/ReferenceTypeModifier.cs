namespace Maho.Syntax;

internal sealed class ReferenceTypeModifier : PostfixTypeModifier
{
    public Token Ampersand { get; }
    public PostfixTypeModifierKind Kind => PostfixTypeModifierKind.Reference;

    public ReferenceTypeModifier(Token ampersand)
    {
        Ampersand = ampersand;
    }
}