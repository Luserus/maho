namespace Maho.Syntax;

/// <summary> Array postfix type modifier for dynamic arrays (<c>[]</c>, <c>[,]</c>, etc.). </summary>
internal sealed class ArrayTypeModifier : PostfixTypeModifier
{
    /// <summary> Opening bracket token. </summary>
    public Token LeftBracket { get; }
    /// <summary> Closing bracket token. </summary>
    public Token RightBracket { get; }
    /// <summary> Array dimension / rank count (e.g. 1 for [], 2 for [,], 3 for [,,]). </summary>
    public int Dimension { get; }

    /// <summary> Creates one array modifier node. </summary>
    public ArrayTypeModifier(Token leftBracket, Token rightBracket, int dimension = 1) : base(PostfixTypeModifierKind.Array)
    {
        LeftBracket = leftBracket;
        RightBracket = rightBracket;
        Dimension = dimension;
    }
}
