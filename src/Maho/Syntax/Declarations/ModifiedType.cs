namespace Maho.Syntax;

/// <summary> Type syntax with an optional postfix modifier such as <c>[]</c>, <c>*</c>, <c>&amp;</c>, or <c>?</c>. </summary>
internal sealed class ModifiedType : TypeSyntax
{
    /// <summary> Base type before the postfix modifier is applied. </summary>
    public TypeSyntax Type { get; }
    /// <summary> Postfix modifier. </summary>
    public PostfixTypeModifier Modifier { get; }

    /// <summary> Creates one modified type node. </summary>
    public ModifiedType(TypeSyntax type, PostfixTypeModifier modifier)
    {
        Type = type;
        Modifier = modifier;
    }
}
