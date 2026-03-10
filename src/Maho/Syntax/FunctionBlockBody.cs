using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class FunctionBlockBody : FunctionBody
{
    public Token OpenBrace { get; }
    public IReadOnlyList<Statement> Statements { get; }
    public Token CloseBrace { get; }

    public FunctionBlockBody(Token openBrace, IReadOnlyList<Statement> statements, Token closeBrace)
    {
        OpenBrace = openBrace;
        Statements = statements;
        CloseBrace = closeBrace;
    }
}