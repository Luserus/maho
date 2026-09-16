namespace Maho.Syntax;

internal sealed class MemberAttributeDeclaration : Member
{
    public AttributeSignature Attribute { get; }

    public MemberAttributeDeclaration(AttributeSignature attribute) => Attribute = attribute;
}
