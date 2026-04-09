namespace Maho.Syntax;

/// <summary> Assignment clause that pairs an assignment operator with its initializer expression. </summary>
internal sealed class AssignmentClause : SyntaxNode
{
    /// <summary> Assignment operator token. </summary>
    public Token AssignmentOperator { get; }
    /// <summary> Expression assigned on the right-hand side. </summary>
    public Expression Initializer { get; }

    /// <summary> Creates one assignment clause node. </summary>
    public AssignmentClause(Token assignmentOp, Expression initializer)
    {
        AssignmentOperator = assignmentOp;
        Initializer = initializer;
    }
}
