namespace Maho.Syntax;

internal sealed class LocalReturnStatement : LocalStatement
{
    public ReturnStatement Statement { get; }

    public LocalReturnStatement(ReturnStatement statement) => Statement = statement;
}
