namespace Maho.Syntax;

/// <summary> Fixed-size array postfix type modifier (<c>[size]</c>). </summary>
internal sealed class FixedArrayTypeModifier : PostfixTypeModifier
{
    /// <summary> Opening bracket token. </summary>
    public Token LeftBracket { get; }
    /// <summary> Array size expression. </summary>
    public Expression Size { get; }
    /// <summary> Closing bracket token. </summary>
    public Token RightBracket { get; }

    /// <summary> Creates one fixed-size array modifier node. </summary>
    public FixedArrayTypeModifier(Token leftBracket, Expression size, Token rightBracket) : base(PostfixTypeModifierKind.FixedArray)
    {
        LeftBracket = leftBracket;
        Size = size;
        RightBracket = rightBracket;
    }
}

