namespace Maho.Syntax;

internal sealed class MemberAttributeDeclaration : Member
{
    public AttributeSignature Attribute { get; }
    public Token Semicolon { get; }

    public MemberAttributeDeclaration(AttributeSignature attribute, Token semicolon)
    {
        Attribute = attribute;
        Semicolon = semicolon;
    }
}