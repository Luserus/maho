namespace Maho.Syntax;

/// <summary> Span postfix type modifier (<c>T[&amp;]</c>). </summary>
internal sealed class SpanTypeModifier : PostfixTypeModifier
{
    /// <summary> Open bracket token <c>[</c>. </summary>
    public Token OpenBracket { get; }

    /// <summary> Ampersand token <c>&amp;</c>. </summary>
    public Token Ampersand { get; }

    /// <summary> Close bracket token <c>]</c>. </summary>
    public Token CloseBracket { get; }

    /// <summary> Creates one span modifier node. </summary>
    public SpanTypeModifier(Token openBracket, Token ampersand, Token closeBracket) : base(PostfixTypeModifierKind.Span)
    {
        OpenBracket = openBracket;
        Ampersand = ampersand;
        CloseBracket = closeBracket;
    }
}
