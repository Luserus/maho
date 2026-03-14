namespace Maho.Syntax;

internal sealed class LocalElseStatement : LocalStatement
{
    public Token Keyword { get; }
    public LocalStatement Statement { get; }

    public LocalElseStatement(Token keyword, LocalStatement statement)
    {
        Keyword = keyword;
        Statement = statement;
    }
}