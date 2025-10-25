namespace Maho.Syntax;

internal sealed class EmptyStatement : Statement
{
    public Token Semicolon { get; }

    public EmptyStatement(Token semicolon) => Semicolon = semicolon;
}