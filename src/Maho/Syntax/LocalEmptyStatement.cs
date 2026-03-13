namespace Maho.Syntax;

internal sealed class LocalEmptyStatement : LocalStatement
{
    public Token Semicolon { get; }

    public LocalEmptyStatement(Token semicolon) => Semicolon = semicolon;
}