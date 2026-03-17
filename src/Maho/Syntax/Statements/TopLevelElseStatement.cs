namespace Maho.Syntax;

internal sealed class TopLevelElseStatement : TopLevelStatement
{
    public Token Keyword { get; }
    public TopLevelStatement Statement { get; }

    public TopLevelElseStatement(Token keyword, TopLevelStatement statement)
    {
        Keyword = keyword;
        Statement = statement;
    }
}