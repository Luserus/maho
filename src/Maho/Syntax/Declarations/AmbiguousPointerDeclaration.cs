namespace Maho.Syntax;

/// <summary> Pointer-shaped declaration that is syntactically ambiguous with an expression statement. </summary>
internal sealed class AmbiguousPointerDeclaration : SyntaxNode
{
    /// <summary> Parsed pointer type. </summary>
    public TypeSyntax Type { get; }
    /// <summary> Declared variable name. </summary>
    public NamedSyntax Identifier { get; }

    /// <summary> Creates one ambiguous pointer declaration node. </summary>
    public AmbiguousPointerDeclaration(TypeSyntax type, NamedSyntax identifier)
    {
        Type = type;
        Identifier = identifier;
    }
}
