namespace Maho.Syntax;

/// <summary> Abstract base syntax node for all prefix type modifiers. </summary>
internal abstract class PrefixTypeModifier : SyntaxNode
{
    /// <summary> Prefix modifier kind. </summary>
    public PrefixTypeModifierKind Kind { get; }

    /// <summary> Initializes a new prefix modifier. </summary>
    public PrefixTypeModifier(PrefixTypeModifierKind kind) => Kind = kind;
}

