namespace Maho.Syntax;

/// <summary>
/// Prefix modifier for unsafe storage aliases (<c>*Type</c> or <c>mut *Type</c>).
/// </summary>
internal sealed class UnsafeAliasTypeModifier : PrefixTypeModifier
{
    public Token AsteriskToken { get; }
    public Token? MutToken { get; }
    public bool IsMutable => MutToken is not null;

    public UnsafeAliasTypeModifier(Token asteriskToken, Token? mutToken = null) : base(PrefixTypeModifierKind.UnsafeAlias)
    {
        AsteriskToken = asteriskToken;
        MutToken = mutToken;
    }
}

