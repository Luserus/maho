namespace Maho.Syntax;

internal sealed class ParameterVariableDeclaration : Parameter
{
    public ParameterVariableDeclaration(ParameterVariableDeclarator declarator, AssignmentClause? initializer) : base(declarator, initializer)
    { }
}
