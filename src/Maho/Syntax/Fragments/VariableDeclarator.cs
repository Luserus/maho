namespace Maho.Syntax;

internal sealed class VariableDeclarator : SyntaxNode
{
    public NamedSyntax Identifier { get; }
    public AssignmentClause? Initializer { get; }

    public VariableDeclarator(NamedSyntax identifier, AssignmentClause? initializer)
    {
        Identifier = identifier;
        Initializer = initializer;
    }
}