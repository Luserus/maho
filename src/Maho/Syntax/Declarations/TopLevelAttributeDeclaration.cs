namespace Maho.Syntax;

internal sealed class TopLevelAttributeDeclaration : TopLevelDeclaration
{
    public AttributeSignature Attribute { get; }

    public TopLevelAttributeDeclaration(AttributeSignature attribute) => Attribute = attribute;
}