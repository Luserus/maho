using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Base type for type declarations that can appear at top level or inside a type body. </summary>
internal sealed class TypeDeclaration : SyntaxNode
{
    /// <summary> Declaration modifiers, such as visibility or storage modifiers. </summary>
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> The keyword introducing the type declaration. </summary>
    public Token Keyword { get;}
    /// <summary> Declared kind, such as class or struct. </summary>
    public TypeKind Kind { get; }
    /// <summary> Declared name, including any generic parameter list. </summary>
    public NamedSyntax Name { get; }
    /// <summary> Body or terminator for the declaration. </summary>
    public TypeBody Body { get; }

    /// <summary> Creates one type declaration from its parsed components. </summary>
    public TypeDeclaration(IReadOnlyList<Token> modifiers, Token keyword, TypeKind kind, NamedSyntax name, TypeBody body)
    {
        Modifiers = modifiers;
        Keyword = keyword;
        Kind = kind;
        Name = name;
        Body = body;
    }
}
