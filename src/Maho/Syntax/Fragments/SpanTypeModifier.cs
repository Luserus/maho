namespace Maho.Syntax;

/// <summary> Span prefix type modifier (<c>&amp;T</c>). </summary>
internal sealed class SpanTypeModifier : PrefixTypeModifier
{
    /// <summary> Ampersand token. </summary>
    public Token Ampersand { get; }

    /// <summary> Creates one span modifier node. </summary>
    public SpanTypeModifier(Token ampersand) : base(PrefixTypeModifierKind.Span)
    {
        Ampersand = ampersand;
    }
}

