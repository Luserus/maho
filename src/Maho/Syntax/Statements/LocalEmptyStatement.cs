namespace Maho.Syntax;

/// <summary> Local empty statement represented by a semicolon. </summary>
internal sealed class LocalEmptyStatement : LocalStatement
{
    /// <summary> The terminating semicolon token. </summary>
    public Token Semicolon { get; }

    /// <summary> Creates one local empty statement node. </summary>
    public LocalEmptyStatement(Token semicolon) => Semicolon = semicolon;
}
