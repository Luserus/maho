namespace Maho.Syntax;

internal sealed class AssignmentClause : ISyntaxNode
{
    public Token AssignmentOperator { get; }
    public Expression Initializer { get; }

    public AssignmentClause(Token assignmentOp, Expression initializer)
    {
        AssignmentOperator = assignmentOp;
        Initializer = initializer;
    }
}