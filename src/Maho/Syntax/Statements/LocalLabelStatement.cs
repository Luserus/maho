namespace Maho.Syntax;

/// <summary>A statement label declared with the <c>name:</c> form.</summary>
internal sealed class LocalLabelStatement : LocalStatement
{
    public Token Identifier { get; }
    public Token Colon { get; }

    public LocalLabelStatement(Token identifier, Token colon)
    {
        Identifier = identifier;
        Colon = colon;
    }
}
