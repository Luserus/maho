namespace Maho.Syntax;

internal sealed class LocalAttributeDeclaration : LocalDeclaration
{
    public AttributeSignature Attribute { get; }
    public Token Semicolon { get; }

    public LocalAttributeDeclaration(AttributeSignature attribute, Token semicolon)
    {
        Attribute = attribute;
        Semicolon = semicolon;
    }
}