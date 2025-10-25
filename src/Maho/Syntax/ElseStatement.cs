namespace Maho.Syntax;

internal sealed class ElseStatement : Statement
{
    public Token ElseKeyword { get; }
    public Statement Statement { get; }

    public ElseStatement(Token elseKeyword, Statement statement)
    {
        ElseKeyword = elseKeyword;
        Statement = statement;
    }
}