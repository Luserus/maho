using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Parsed variable declaration shared by top-level variables, fields, and local variables. </summary>
internal sealed class VariableDeclaration : SyntaxNode
{
    /// <summary> Attributes attached to the variable declaration. </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    /// <summary> Modifiers attached to the declaration. </summary>
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> The type of the variable. </summary>
    public TypeSyntax Type { get; }
    /// <summary> Declared variable name. </summary>
    public NamedSyntax Identifier { get; }
    /// <summary> Optional initializer for the variable. </summary>
    public AssignmentClause? Initializer { get; }

    /// <summary> Creates one variable declaration. </summary>
    public VariableDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, TypeSyntax type, NamedSyntax identifier, AssignmentClause? initializer)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        Type = type;
        Identifier = identifier;
        Initializer = initializer;
    }
}
