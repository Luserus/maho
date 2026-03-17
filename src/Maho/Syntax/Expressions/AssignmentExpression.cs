namespace Maho.Syntax;

/// <summary> Represents an assignment expression node. </summary>
internal sealed class AssignmentExpression : Expression
{
    /// <summary> The identifier expression to which the expression is being assigned to. </summary>
    public Expression LhsExpression { get; }
    /// <summary> The assignment operator. </summary>
    public Token EqualsOperator { get; }
    /// <summary> The expression to be assigned. </summary>
    public Expression RhsExpression { get; }

    /// <summary> Initializes the AssignmentExpressionSyntax class. </summary>
    /// <param name="lhsExpression"> The expression to which the rhsExpression is being assigned to. </param>
    /// <param name="equalsOperator"> The assignment operator. </param>
    /// <param name="rhsExpression"> The expression to be assigned. </param>
    public AssignmentExpression(Expression lhsExpression, Token equalsOperator, Expression rhsExpression)
    {
        LhsExpression = lhsExpression;
        EqualsOperator = equalsOperator;
        RhsExpression = rhsExpression;
    }
}