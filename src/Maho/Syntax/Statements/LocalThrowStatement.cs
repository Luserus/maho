namespace Maho.Syntax;

/// <summary> Local wrapper around a throw statement. </summary>
internal sealed class LocalThrowStatement : LocalStatement
{
    /// <summary> Wrapped throw statement. </summary>
    public ThrowStatement Statement { get; }

    public LocalThrowStatement(ThrowStatement statement) => Statement = statement;
}

