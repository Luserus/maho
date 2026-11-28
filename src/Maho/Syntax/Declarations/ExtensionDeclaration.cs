using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Represents an extension declaration extending an existing type. </summary>
internal sealed class ExtensionDeclaration : SyntaxNode
{
    /// <summary> Attributes attached to the extension declaration. </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    /// <summary> Declaration modifiers (e.g. public, internal). </summary>
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> The 'extension' keyword token. </summary>
    public Token Keyword { get; }
    public Token? LessThanToken { get; }
    public SeparatedSyntaxList<GenericParameterSyntax>? GenericParameters { get; }
    public Token? GreaterThanToken { get; }
    /// <summary> Target type being extended. </summary>
    public TypeSyntax TargetType { get; }
    /// <summary> Base interfaces implemented by this extension. </summary>
    public TypeBaseClause? Base { get; }
    /// <summary> Constraints on extension generic parameters. </summary>
    public IReadOnlyList<TypeConstraintClause> Constraints { get; }
    /// <summary> Members declared inside the extension body. </summary>
    public TypeBody Body { get; }

    public ExtensionDeclaration(
        IReadOnlyList<AttributeListSyntax> attributes,
        IReadOnlyList<Token> modifiers,
        Token keyword,
        Token? lessThanToken,
        SeparatedSyntaxList<GenericParameterSyntax>? genericParameters,
        Token? greaterThanToken,
        TypeSyntax targetType,
        TypeBaseClause? @base,
        IReadOnlyList<TypeConstraintClause> constraints,
        TypeBody body)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        Keyword = keyword;
        LessThanToken = lessThanToken;
        GenericParameters = genericParameters;
        GreaterThanToken = greaterThanToken;
        TargetType = targetType;
        Base = @base;
        Constraints = constraints;
        Body = body;
    }
}

