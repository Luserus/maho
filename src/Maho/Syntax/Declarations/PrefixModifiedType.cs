namespace Maho.Syntax;

/// <summary> Type syntax with a prefix modifier such as <c>&amp;T</c> (Span). </summary>
internal sealed class PrefixModifiedType : TypeSyntax
{
    /// <summary> Prefix modifier. </summary>
    public PrefixTypeModifier Modifier { get; }
    /// <summary> Base type after the prefix modifier. </summary>
    public TypeSyntax Type { get; }

    /// <summary> Creates one prefix modified type node. </summary>
    public PrefixModifiedType(PrefixTypeModifier modifier, TypeSyntax type)
    {
        Modifier = modifier;
        Type = type;
    }
}

