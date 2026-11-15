namespace Maho.Syntax;

/// <summary>
/// Prefix modifier that introduces an alias type (<c>ref T</c> or <c>mut ref T</c>).
/// </summary>
internal sealed class RefTypeModifier : PrefixTypeModifier
{
    /// <summary> Optional 'mut' token before 'ref'. </summary>
    public Token? MutKeyword { get; }

    /// <summary> The 'ref' keyword token. </summary>
    public Token RefKeyword { get; }

    /// <summary> Indicates whether this alias is mutable (<c>mut ref T</c>). </summary>
    public bool IsMutable => MutKeyword != null;

    public RefTypeModifier(Token? mutKeyword, Token refKeyword)
        : base(PrefixTypeModifierKind.Ref)
    {
        MutKeyword = mutKeyword;
        RefKeyword = refKeyword;
    }
}

