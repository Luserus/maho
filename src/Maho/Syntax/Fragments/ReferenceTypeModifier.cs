namespace Maho.Syntax;

/// <summary> Reference postfix type modifier (<c>T&amp;</c> or <c>T mut&amp;</c>). </summary>
internal sealed class ReferenceTypeModifier : PostfixTypeModifier
{
    /// <summary> Optional <c>mut</c> token for mutable references (<c>T mut&amp;</c>). </summary>
    public Token? MutToken { get; }

    /// <summary> Ampersand token <c>&amp;</c>. </summary>
    public Token Ampersand { get; }

    /// <summary> Gets whether this is a mutable reference (<c>T mut&amp;</c>). </summary>
    public bool IsMutable => MutToken is not null;

    /// <summary> Creates one reference modifier node. </summary>
    public ReferenceTypeModifier(Token ampersand, Token? mutToken = null) : base(PostfixTypeModifierKind.Reference)
    {
        Ampersand = ampersand;
        MutToken = mutToken;
    }
}
