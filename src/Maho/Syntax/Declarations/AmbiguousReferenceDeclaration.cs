namespace Maho.Syntax;

/// <summary> Reference-shaped declaration that is syntactically ambiguous with an expression statement. </summary>
internal sealed class AmbiguousReferenceDeclaration : SyntaxNode
{
    /// <summary> Parsed reference type. </summary>
    public TypeSyntax Type { get; }
    /// <summary> Declared variable name. </summary>
    public NamedSyntax Identifier { get; }

    /// <summary> Creates one ambiguous reference declaration node. </summary>
    public AmbiguousReferenceDeclaration(TypeSyntax type, NamedSyntax identifier)
    {
        Type = type;
        Identifier = identifier;
    }
}
