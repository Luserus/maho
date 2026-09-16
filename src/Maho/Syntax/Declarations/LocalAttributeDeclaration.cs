namespace Maho.Syntax;

internal sealed class LocalAttributeDeclaration : LocalDeclaration
{
    public AttributeSignature Attribute { get; }

    public LocalAttributeDeclaration(AttributeSignature attribute) => Attribute = attribute;
}