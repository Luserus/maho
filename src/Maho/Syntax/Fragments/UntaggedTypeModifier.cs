namespace Maho.Syntax;

/// <summary> Untagged prefix type modifier (<c>untagged T</c>). </summary>
internal sealed class UntaggedTypeModifier : PrefixTypeModifier
{
    /// <summary> Untagged keyword token. </summary>
    public Token UntaggedKeyword { get; }

    /// <summary> Creates one untagged modifier node. </summary>
    public UntaggedTypeModifier(Token untaggedKeyword) : base(PrefixTypeModifierKind.Untagged)
    {
        UntaggedKeyword = untaggedKeyword;
    }
}

