namespace Maho.Syntax;

/// <summary> Namespace body represented by a trailing semicolon. </summary>
internal sealed class NamespaceEmptyBody : NamespaceBody
{
    /// <summary> Semicolon terminator token. </summary>
    public Token Semicolon { get; }

    /// <summary> Creates one empty namespace body node. </summary>
    public NamespaceEmptyBody(Token semicolon) => Semicolon = semicolon;
}
