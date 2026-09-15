namespace Maho.Syntax;

/// <summary> Base type for postfix type modifiers such as arrays, pointers, references, and optionals. </summary>
internal abstract class PostfixTypeModifier : SyntaxNode
{
    /// <summary> Modifier kind value. </summary>
    public PostfixTypeModifierKind Kind { get; }

    public PostfixTypeModifier(PostfixTypeModifierKind kind) => Kind = kind;
}
