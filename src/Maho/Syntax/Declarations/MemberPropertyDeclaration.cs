using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Member declaration that introduces one property. </summary>
internal sealed class MemberPropertyDeclaration : Member
{
    /// <summary> Attributes attached to the property declaration. </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    /// <summary> Modifiers attached to the property declaration. </summary>
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> Declared property type. </summary>
    public TypeSyntax Type { get; }
    /// <summary> Declared property name. </summary>
    public NamedSyntax Identifier { get; }
    /// <summary> Accessor body for the property. </summary>
    public PropertyAccessorList Body { get; }

    /// <summary> Creates one member-property declaration node. </summary>
    public MemberPropertyDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, TypeSyntax type, NamedSyntax identifier, PropertyAccessorList body)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        Type = type;
        Identifier = identifier;
        Body = body;
    }
}
