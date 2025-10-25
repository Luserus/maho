namespace Maho.Syntax;

internal sealed class VariableDeclarator : ISyntaxNode
{
    public IdentifierName Identifier { get; }
    public AssignmentClause? Initializer { get; }

    public VariableDeclarator(IdentifierName identifier, AssignmentClause? initializer)
    {
        Identifier = identifier;
        Initializer = initializer;
    }
}