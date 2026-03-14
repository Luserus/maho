using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class TopLevelBlockStatement : TopLevelStatement
{
    public Token OpenBrace { get; }
    public IReadOnlyList<Local> Locals { get; }
    public Token CloseBrace { get; }

    public TopLevelBlockStatement(Token openBrace, IReadOnlyList<Local> locals, Token closeBrace)
    {
        OpenBrace = openBrace;
        Locals = locals;
        CloseBrace = closeBrace;
    }
}