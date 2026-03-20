namespace Maho.Syntax;

internal sealed class NamespaceEmptyBody : NamespaceBody
{
    public Token Semicolon { get; }

    public NamespaceEmptyBody(Token semicolon) => Semicolon = semicolon;
}