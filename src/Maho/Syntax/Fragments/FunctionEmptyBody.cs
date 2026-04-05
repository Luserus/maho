namespace Maho.Syntax;

/// <summary> Function body represented by a trailing semicolon. </summary>
internal sealed class FunctionEmptyBody : FunctionBody
{
    /// <summary> Semicolon terminator token. </summary>
    public Token Semicolon { get; }

    /// <summary> Creates one empty function body node. </summary>
    public FunctionEmptyBody(Token semicolon)
    {
        Semicolon = semicolon;
    }
}
