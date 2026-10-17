namespace Maho.Syntax;

/// <summary> Represents one element in a tuple type, consisting of a type and an optional element name. </summary>
internal sealed class TupleTypeElement : SyntaxNode
{
    /// <summary> The type of this tuple element. </summary>
    public TypeSyntax Type { get; }

    /// <summary> Optional identifier name for this tuple element. </summary>
    public Token? Name { get; }

    public TupleTypeElement(TypeSyntax type, Token? name = null)
    {
        Type = type;
        Name = name;
    }
}
