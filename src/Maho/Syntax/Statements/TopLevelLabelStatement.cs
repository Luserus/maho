namespace Maho.Syntax;

/// <summary>A label in a pragma-enabled top-level statement body.</summary>
internal sealed class TopLevelLabelStatement : TopLevelStatement
{
    public Token Identifier { get; }
    public Token Colon { get; }

    public TopLevelLabelStatement(Token identifier, Token colon)
    {
        Identifier = identifier;
        Colon = colon;
    }
}
