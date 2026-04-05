namespace Maho.Syntax;

/// <summary> Top-level else branch that attaches to an enclosing if statement. </summary>
internal sealed class TopLevelElseStatement : TopLevelStatement
{
    /// <summary> The else keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Nested statement guarded by the else branch. </summary>
    public TopLevelStatement Statement { get; }

    /// <summary> Creates one top-level else statement node. </summary>
    public TopLevelElseStatement(Token keyword, TopLevelStatement statement)
    {
        Keyword = keyword;
        Statement = statement;
    }
}
