namespace Maho.Syntax;

/// <summary> Statement that yields a value or breaks from a generator (<c>yield expr;</c> or <c>yield break;</c>). </summary>
internal sealed class LocalYieldStatement : LocalStatement
{
    /// <summary> The yield keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Indicates whether this statement is a generator break (<c>yield break;</c>). </summary>
    public bool IsBreak { get; }
    /// <summary> Optional expression yielded to the caller. </summary>
    public Expression? Expression { get; }
    /// <summary> Semicolon token terminating the statement. </summary>
    public Token Semicolon { get; }

    public LocalYieldStatement(Token keyword, bool isBreak, Expression? expression, Token semicolon)
    {
        Keyword = keyword;
        IsBreak = isBreak;
        Expression = expression;
        Semicolon = semicolon;
    }
}
