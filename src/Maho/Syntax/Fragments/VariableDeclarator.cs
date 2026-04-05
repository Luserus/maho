namespace Maho.Syntax;

/// <summary> One variable declarator within a larger variable declaration. </summary>
internal sealed class VariableDeclarator : SyntaxNode
{
    /// <summary> Declared variable name. </summary>
    public NamedSyntax Identifier { get; }
    /// <summary> Optional initializer for the declarator. </summary>
    public AssignmentClause? Initializer { get; }

    /// <summary> Creates one variable declarator node. </summary>
    public VariableDeclarator(NamedSyntax identifier, AssignmentClause? initializer)
    {
        Identifier = identifier;
        Initializer = initializer;
    }
}
