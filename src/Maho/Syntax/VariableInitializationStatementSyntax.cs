namespace Maho.Syntax;

/// <summary> Represents a variable initialization statement node. </summary>
/// <param name="type"> The type of the variable. </param>
/// <param name="assignmentExpression"> The assignment expression as variable initializer. </param>
/// <param name="semicolon"> The semicolon to mark the end of the statement. </param>
internal sealed class VariableInitializationStatementSyntax(Token type, AssignmentExpressionSyntax assignmentExpression, Token semicolon) : StatementSyntax
{
    /// <summary> The type of the variable. </summary>
    public Token Type { get; } = type;
    /// <summary> The assignment expression as variable initializer. </summary>
    public AssignmentExpressionSyntax AssignmentExpression { get; } = assignmentExpression;
    /// <summary> The semicolon to mark the end of the statement. </summary>
    public Token Semicolon { get; } = semicolon;
}