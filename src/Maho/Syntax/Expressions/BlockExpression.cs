using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Represents a block expression containing multiple statements. </summary>
internal sealed class BlockExpression : Expression
{
    public Token OpenBrace { get; }
    public IReadOnlyList<Local> Locals { get; }
    public Expression? FinalExpression { get; }
    public Token CloseBrace { get; }

    public BlockExpression(Token openBrace, IReadOnlyList<Local> locals, Expression? finalExpression, Token closeBrace)
    {
        OpenBrace = openBrace;
        Locals = locals;
        FinalExpression = finalExpression;
        CloseBrace = closeBrace;
    }
}