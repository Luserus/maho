namespace Maho.Syntax;

/// <summary>An unconditional branch to a label in the current function.</summary>
internal sealed class LocalGotoStatement : LocalStatement
{
    public Token Keyword { get; }
    public Token Identifier { get; }
    public Token Semicolon { get; }

    public LocalGotoStatement(Token keyword, Token identifier, Token semicolon)
    {
        Keyword = keyword;
        Identifier = identifier;
        Semicolon = semicolon;
    }
}
