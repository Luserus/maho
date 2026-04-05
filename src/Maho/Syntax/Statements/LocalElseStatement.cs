namespace Maho.Syntax;

/// <summary> Local else branch that attaches to an enclosing if statement. </summary>
internal sealed class LocalElseStatement : LocalStatement
{
    /// <summary> The else keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Nested statement guarded by the else branch. </summary>
    public LocalStatement Statement { get; }

    /// <summary> Creates one local else statement node. </summary>
    public LocalElseStatement(Token keyword, LocalStatement statement)
    {
        Keyword = keyword;
        Statement = statement;
    }
}
