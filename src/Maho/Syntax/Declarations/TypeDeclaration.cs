using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Base type for type declarations that can appear at top level or inside a type body. </summary>
internal sealed class TypeDeclaration : SyntaxNode
{
    /// <summary> Attributes attached to the type declaration. </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    /// <summary> Declaration modifiers, such as visibility or storage modifiers. </summary>
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> The keyword introducing the type declaration. </summary>
    public Token Keyword { get; }
    /// <summary> Declared kind, such as class or struct. </summary>
    public TypeKind Kind { get; }
    /// <summary> Declared name, including any generic parameter list. </summary>
    public NamedSyntax Name { get; }
    /// <summary> Optional discriminant type for enums specified via 'with <Type>'. </summary>
    public TypeSyntax? DiscriminantType { get; }
    /// <summary> Constituent types for union declarations, e.g. (A | B | C). </summary>
    public SeparatedSyntaxList<TypeSyntax>? UnionConstituents { get; }
    /// <summary> Body or terminator for the declaration. </summary>
    public TypeBaseClause? Base { get; }
    public IReadOnlyList<TypeConstraintClause> Constraints { get; }
    public TypeBody Body { get; }

    /// <summary> Creates one type declaration from its parsed components. </summary>
    public TypeDeclaration(
        IReadOnlyList<AttributeListSyntax> attributes,
        IReadOnlyList<Token> modifiers,
        Token keyword,
        TypeKind kind,
        NamedSyntax name,
        TypeBaseClause? @base,
        IReadOnlyList<TypeConstraintClause> constraints,
        TypeBody body,
        TypeSyntax? discriminantType = null,
        SeparatedSyntaxList<TypeSyntax>? unionConstituents = null)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        Keyword = keyword;
        Kind = kind;
        Name = name;
        Base = @base;
        Constraints = constraints;
        Body = body;
        DiscriminantType = discriminantType;
        UnionConstituents = unionConstituents;
    }
}