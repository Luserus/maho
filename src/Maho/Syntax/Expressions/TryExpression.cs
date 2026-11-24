using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Represents a try-catch-finally expression: <c>try { ... } catch (...) { ... } finally { ... }</c>. </summary>
internal sealed class TryExpression : Expression
{
    public Token TryKeyword { get; }
    public BlockExpression TryBlock { get; }
    public IReadOnlyList<CatchClause> CatchClauses { get; }
    public FinallyClause? FinallyClause { get; }

    public TryExpression(
        Token tryKeyword,
        BlockExpression tryBlock,
        IReadOnlyList<CatchClause> catchClauses,
        FinallyClause? finallyClause)
    {
        TryKeyword = tryKeyword;
        TryBlock = tryBlock;
        CatchClauses = catchClauses;
        FinallyClause = finallyClause;
    }
}

