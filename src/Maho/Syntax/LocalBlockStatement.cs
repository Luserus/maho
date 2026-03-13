using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class LocalBlockStatement : LocalStatement
{
    public Token OpenBrace { get; }
    public IReadOnlyList<LocalStatement> Statements { get; }
    public Token CloseBrace { get; }

    public LocalBlockStatement(Token openBrace, IReadOnlyList<LocalStatement> statements, Token closeBrace)
    {
        OpenBrace = openBrace;
        Statements = statements;
        CloseBrace = closeBrace;
    }
}