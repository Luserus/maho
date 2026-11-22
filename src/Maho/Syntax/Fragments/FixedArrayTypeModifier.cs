namespace Maho.Syntax;

/// <summary> Fixed-size array postfix type modifier (<c>[size1, size2, ...]</c>). </summary>
internal sealed class FixedArrayTypeModifier : PostfixTypeModifier
{
    /// <summary> Opening bracket token. </summary>
    public Token LeftBracket { get; }
    /// <summary> Array size expressions. </summary>
    public SeparatedSyntaxList<Expression> Sizes { get; }
    /// <summary> Primary array size expression. </summary>
    public Expression Size => Sizes[0];
    /// <summary> Dimension / rank of the fixed array. </summary>
    public int Dimension => Sizes.Count;
    /// <summary> Closing bracket token. </summary>
    public Token RightBracket { get; }

    /// <summary> Creates one fixed-size array modifier node with comma-separated sizes. </summary>
    public FixedArrayTypeModifier(Token leftBracket, SeparatedSyntaxList<Expression> sizes, Token rightBracket) : base(PostfixTypeModifierKind.FixedArray)
    {
        LeftBracket = leftBracket;
        Sizes = sizes;
        RightBracket = rightBracket;
    }

    /// <summary> Creates one 1D fixed-size array modifier node. </summary>
    public FixedArrayTypeModifier(Token leftBracket, Expression size, Token rightBracket) : base(PostfixTypeModifierKind.FixedArray)
    {
        LeftBracket = leftBracket;
        Sizes = new SeparatedSyntaxList<Expression>(new SyntaxNode[] { size });
        RightBracket = rightBracket;
    }
}

