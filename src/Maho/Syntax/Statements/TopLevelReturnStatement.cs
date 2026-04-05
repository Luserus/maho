namespace Maho.Syntax;

/// <summary> Top-level return statement. </summary>
internal sealed class TopLevelReturnStatement : TopLevelStatement
{
    /// <summary> Wrapped return statement payload. </summary>
    public ReturnStatement Statement { get; }

    /// <summary> Creates one top-level return statement node. </summary>
    public TopLevelReturnStatement(ReturnStatement statement) => Statement = statement;
}
