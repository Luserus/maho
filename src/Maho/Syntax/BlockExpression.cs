using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Represents a block expression containing multiple statements. </summary>
internal sealed class BlockExpression : Expression
{
    public Token OpenBrace { get; }
    public IReadOnlyList<LocalStatement> Statements { get; }
    public Expression? FinalExpression { get; }
    public Token CloseBrace { get; }

    public BlockExpression(Token openBrace, IReadOnlyList<LocalStatement> statements, Expression? finalExpression, Token closeBrace)
    {
        OpenBrace = openBrace;
        Statements = statements;
        FinalExpression = finalExpression;
        CloseBrace = closeBrace;
    }
}