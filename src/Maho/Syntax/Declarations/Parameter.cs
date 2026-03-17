namespace Maho.Syntax;

internal sealed class Parameter : SyntaxNode
{
    public ParameterVariableDeclarator Declarator { get; }
    public AssignmentClause? Initializer { get; }

    public Parameter(ParameterVariableDeclarator declarator, AssignmentClause? initializer)
    {
        Declarator = declarator;
        Initializer = initializer;
    }
}