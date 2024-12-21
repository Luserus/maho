namespace Maho.Syntax;

/// <summary> Represents an assignment expression node. </summary>
/// <param name="identifierExpression"> The identifier expression to which the expression is being assigned to. </param>
/// <param name="equalsOperator"> The assignment operator. </param>
/// <param name="expression"> The expression to be assigned. </param>
internal sealed class AssignmentExpressionSyntax(IdentifierExpressionSyntax identifierExpression, Token equalsOperator, ExpressionSyntax expression) : ExpressionSyntax
{
    /// <summary> The identifier expression to which the expression is being assigned to. </summary>
    public IdentifierExpressionSyntax IdentifierExpression { get; } = identifierExpression;
    /// <summary> The assignment operator. </summary>
    public Token EqualsOperator { get; } = equalsOperator;
    /// <summary> The expression to be assigned. </summary>
    public ExpressionSyntax Expression { get; } = expression;
}