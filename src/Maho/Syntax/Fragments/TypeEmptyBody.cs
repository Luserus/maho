namespace Maho.Syntax;

/// <summary> Type body represented by a trailing semicolon. </summary>
internal sealed class TypeEmptyBody : TypeBody
{
    /// <summary> Semicolon terminator token. </summary>
    public Token Semicolon { get; }

    /// <summary> Creates one empty type body node. </summary>
    public TypeEmptyBody(Token semicolon) => Semicolon = semicolon;
}
