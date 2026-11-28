namespace Maho.Syntax;

/// <summary> Represents a single entry in a type or extension base clause with an optional visibility modifier. </summary>
internal sealed class BaseTypeItemSyntax : SyntaxNode
{
    /// <summary> Optional accessibility modifier (<c>public</c>, <c>internal</c>, <c>private</c>, or <c>protected</c>). </summary>
    public Token? VisibilityModifier { get; }

    /// <summary> The base class or implemented interface type. </summary>
    public TypeSyntax Type { get; }

    public BaseTypeItemSyntax(Token? visibilityModifier, TypeSyntax type)
    {
        VisibilityModifier = visibilityModifier;
        Type = type;
    }
}

