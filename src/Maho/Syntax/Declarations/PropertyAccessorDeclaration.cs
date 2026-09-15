using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> One accessor declared inside a property body. </summary>
internal sealed class PropertyAccessorDeclaration : SyntaxNode
{
    /// <summary> Attributes attached directly to the accessor. </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    /// <summary> Modifiers attached directly to the accessor. </summary>
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> Accessor keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Normalized accessor kind. </summary>
    public PropertyAccessorKind Kind { get; }
    /// <summary> Accessor body, using the same body forms as functions. </summary>
    public FunctionBody Body { get; }

    /// <summary> Creates one property accessor declaration. </summary>
    public PropertyAccessorDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, Token keyword, PropertyAccessorKind kind, FunctionBody body)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        Keyword = keyword;
        Kind = kind;
        Body = body;
    }
}
