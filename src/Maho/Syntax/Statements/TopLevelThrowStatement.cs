namespace Maho.Syntax;

/// <summary> Top-level wrapper around a throw statement. </summary>
internal sealed class TopLevelThrowStatement : TopLevelStatement
{
    /// <summary> Wrapped throw statement. </summary>
    public ThrowStatement Statement { get; }

    public TopLevelThrowStatement(ThrowStatement statement) => Statement = statement;
}

