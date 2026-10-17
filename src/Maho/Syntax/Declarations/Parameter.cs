using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Parameter syntax node containing the declared variable and optional default value. </summary>
internal sealed class Parameter : SyntaxNode
{
    /// <summary> Attribute lists attached to the parameter. </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    /// <summary> Variable declarator for the parameter. </summary>
    public ParameterVariableDeclarator Declarator { get; }
    /// <summary> Optional initializer assigned by the parameter declaration. </summary>
    public AssignmentClause? Initializer { get; }

    /// <summary> Creates one parameter node. </summary>
    public Parameter(IReadOnlyList<AttributeListSyntax> attributes, ParameterVariableDeclarator declarator, AssignmentClause? initializer)
    {
        Attributes = attributes;
        Declarator = declarator;
        Initializer = initializer;
    }

    /// <summary> Creates one parameter node without attributes. </summary>
    public Parameter(ParameterVariableDeclarator declarator, AssignmentClause? initializer)
        : this([], declarator, initializer)
    {
    }
}