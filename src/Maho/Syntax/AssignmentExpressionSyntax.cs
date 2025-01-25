namespace Maho.Syntax;

/// <summary> Represents an assignment expression node. </summary>
internal sealed class AssignmentExpressionSyntax : ExpressionSyntax
{
    /// <summary> The identifier expression to which the expression is being assigned to. </summary>
    public IdentifierExpressionSyntax IdentifierExpression { get; }
    /// <summary> The assignment operator. </summary>
    public Token EqualsOperator { get; }
    /// <summary> The expression to be assigned. </summary>
    public ExpressionSyntax Expression { get; }

    /// <summary> Initializes the AssignmentExpressionSyntax class. </summary>
    /// <param name="identifierExpression"> The identifier expression to which the expression is being assigned to. </param>
    /// <param name="equalsOperator"> The assignment operator. </param>
    /// <param name="expression"> The expression to be assigned. </param>
    public AssignmentExpressionSyntax(IdentifierExpressionSyntax identifierExpression, Token equalsOperator, ExpressionSyntax expression)
    {
        IdentifierExpression = identifierExpression;
        EqualsOperator = equalsOperator;
        Expression = expression;
    }
}