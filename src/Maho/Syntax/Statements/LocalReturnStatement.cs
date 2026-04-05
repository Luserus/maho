namespace Maho.Syntax;

/// <summary> Local wrapper around a return statement. </summary>
internal sealed class LocalReturnStatement : LocalStatement
{
    /// <summary> Wrapped return statement. </summary>
    public ReturnStatement Statement { get; }

    /// <summary> Creates one local return statement node. </summary>
    public LocalReturnStatement(ReturnStatement statement) => Statement = statement;
}
