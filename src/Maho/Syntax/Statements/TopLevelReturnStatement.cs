namespace Maho.Syntax;

internal sealed class TopLevelReturnStatement : TopLevelStatement
{
    public ReturnStatement Statement { get; }

    public TopLevelReturnStatement(ReturnStatement statement) => Statement = statement;
}