namespace Maho.Syntax;

/// <summary>An unconditional branch in a pragma-enabled top-level statement body.</summary>
internal sealed class TopLevelGotoStatement : TopLevelStatement
{
    public Token Keyword { get; }
    public Token Identifier { get; }
    public Token Semicolon { get; }

    public TopLevelGotoStatement(Token keyword, Token identifier, Token semicolon)
    {
        Keyword = keyword;
        Identifier = identifier;
        Semicolon = semicolon;
    }
}
