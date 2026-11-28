namespace Maho.Syntax;

/// <summary> A <c>derived Type</c> generic argument inside an interface base clause, marking that position for re-binding on subtypes. </summary>
internal sealed class DerivedGenericArgumentSyntax : TypeSyntax
{
    /// <summary> Contextual <c>derived</c> keyword token. </summary>
    public Token DerivedKeyword { get; }

    /// <summary> Inner target type syntax named after <c>derived</c>. </summary>
    public TypeSyntax TargetType { get; }

    /// <summary> Creates a <c>derived</c> generic argument node. </summary>
    public DerivedGenericArgumentSyntax(Token derivedKeyword, TypeSyntax targetType)
    {
        DerivedKeyword = derivedKeyword;
        TargetType = targetType;
    }
}

