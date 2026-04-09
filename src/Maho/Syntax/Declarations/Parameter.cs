namespace Maho.Syntax;

/// <summary> Parameter syntax node containing the declared variable and optional default value. </summary>
internal sealed class Parameter : SyntaxNode
{
    /// <summary> Variable declarator for the parameter. </summary>
    public ParameterVariableDeclarator Declarator { get; }
    /// <summary> Optional initializer assigned by the parameter declaration. </summary>
    public AssignmentClause? Initializer { get; }

    /// <summary> Creates one parameter node. </summary>
    public Parameter(ParameterVariableDeclarator declarator, AssignmentClause? initializer)
    {
        Declarator = declarator;
        Initializer = initializer;
    }
}
