namespace Maho.Syntax;

internal sealed class TopLevelAttributeDeclaration : TopLevelDeclaration
{
    public AttributeSignature Attribute { get; }
    public Token Semicolon { get; }

    public TopLevelAttributeDeclaration(AttributeSignature attribute, Token semicolon)
    {
        Attribute = attribute;
        Semicolon = semicolon;
    }
}