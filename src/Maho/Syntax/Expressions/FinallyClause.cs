namespace Maho.Syntax;

/// <summary> Represents a finally clause in a try expression: <c>finally { ... }</c>. </summary>
internal sealed class FinallyClause : SyntaxNode
{
    public Token FinallyKeyword { get; }
    public BlockExpression Block { get; }

    public FinallyClause(Token finallyKeyword, BlockExpression block)
    {
        FinallyKeyword = finallyKeyword;
        Block = block;
    }
}

