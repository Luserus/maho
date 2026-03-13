using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class TopLevelBlockStatement : TopLevelStatement
{
    public Token OpenBrace { get; }
    public IReadOnlyList<LocalStatement> Statements { get; }
    public Token CloseBrace { get; }

    public TopLevelBlockStatement(Token openBrace, IReadOnlyList<LocalStatement> statements, Token closeBrace)
    {
        OpenBrace = openBrace;
        Statements = statements;
        CloseBrace = closeBrace;
    }
}