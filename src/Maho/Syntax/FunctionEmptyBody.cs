namespace Maho.Syntax;

internal sealed class FunctionEmptyBody : FunctionBody
{
    public Token Semicolon { get; }

    public FunctionEmptyBody(Token semicolon)
    {
        Semicolon = semicolon;
    }
}