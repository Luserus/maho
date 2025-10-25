using Maho.Syntax;

namespace Maho.Syntax;

internal abstract class Parameter : DeclarationSyntax
{
    public ParameterVariableDeclarator Declarator { get; }
    public AssignmentClause? Initializer { get; }

    public Parameter(ParameterVariableDeclarator declarator, AssignmentClause? initializer)
    {
        Declarator = declarator;
        Initializer = initializer;
    }
}