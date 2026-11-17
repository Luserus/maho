namespace Maho.Syntax;

/// <summary>
/// Prefix modifier for safe storage aliases (<c>&amp;Type</c> or <c>mut &amp;Type</c>).
/// </summary>
internal sealed class SafeAliasTypeModifier : PrefixTypeModifier
{
    public Token AmpersandToken { get; }
    public Token? MutToken { get; }
    public bool IsMutable => MutToken is not null;

    public SafeAliasTypeModifier(Token ampersandToken, Token? mutToken = null) : base(PrefixTypeModifierKind.SafeAlias)
    {
        AmpersandToken = ampersandToken;
        MutToken = mutToken;
    }
}

