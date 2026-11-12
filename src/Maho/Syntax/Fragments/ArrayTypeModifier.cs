namespace Maho.Syntax;

/// <summary> Array postfix type modifier for dynamic arrays (<c>[]</c>). </summary>
internal sealed class ArrayTypeModifier : PostfixTypeModifier
{
    /// <summary> Opening bracket token. </summary>
    public Token LeftBracket { get; }
    /// <summary> Closing bracket token. </summary>
    public Token RightBracket { get; }

    /// <summary> Creates one array modifier node. </summary>
    public ArrayTypeModifier(Token leftBracket, Token rightBracket) : base(PostfixTypeModifierKind.Array)
    {
        LeftBracket = leftBracket;
        RightBracket = rightBracket;
    }
}
