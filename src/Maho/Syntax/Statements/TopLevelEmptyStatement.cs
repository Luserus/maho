namespace Maho.Syntax;

/// <summary> Top-level empty statement represented by a semicolon. </summary>
internal sealed class TopLevelEmptyStatement : TopLevelStatement
{
    /// <summary> The terminating semicolon token. </summary>
    public Token Semicolon { get; }

    /// <summary> Creates one top-level empty statement node. </summary>
    public TopLevelEmptyStatement(Token semicolon) => Semicolon = semicolon;
}
