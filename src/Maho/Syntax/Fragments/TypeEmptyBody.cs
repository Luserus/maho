namespace Maho.Syntax;

internal sealed class TypeEmptyBody : TypeBody
{
    public Token Semicolon { get; }

    public TypeEmptyBody(Token semicolon) => Semicolon = semicolon;
}
