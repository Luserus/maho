namespace Maho.Syntax;

/// <summary> Slice postfix type modifier (<c>[]</c>, <c>[,]</c>, etc.). </summary>
internal sealed class SliceTypeModifier : PostfixTypeModifier
{
    /// <summary> Opening bracket token. </summary>
    public Token LeftBracket { get; }
    /// <summary> Closing bracket token. </summary>
    public Token RightBracket { get; }
    /// <summary> Slice dimension / rank count (e.g. 1 for [], 2 for [,], 3 for [,,]). </summary>
    public int Dimension { get; }

    /// <summary> Creates one slice modifier node. </summary>
    public SliceTypeModifier(Token leftBracket, Token rightBracket, int dimension = 1) : base(PostfixTypeModifierKind.Slice)
    {
        LeftBracket = leftBracket;
        RightBracket = rightBracket;
        Dimension = dimension;
    }
}

