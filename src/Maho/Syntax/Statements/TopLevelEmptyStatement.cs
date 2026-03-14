namespace Maho.Syntax;

internal sealed class TopLevelEmptyStatement : TopLevelStatement
{
    public Token Semicolon { get; }

    public TopLevelEmptyStatement(Token semicolon) => Semicolon = semicolon;
}