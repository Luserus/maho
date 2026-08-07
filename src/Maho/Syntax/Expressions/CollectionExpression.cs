using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Collection literal expression enclosed in brackets. </summary>
internal sealed class CollectionExpression : Expression
{
    /// <summary> Opening bracket token. </summary>
    public Token LeftBracket { get; }
    /// <summary> Elements inside the collection literal. </summary>
    public SeparatedSyntaxList<Expression> Expressions { get; }
    /// <summary> Closing bracket token. </summary>
    public Token RightBracket { get; }
    /// <summary> Trailing collection expression modifiers. </summary>
    public IReadOnlyList<CollectionExpressionModifier> Modifiers { get; }

    /// <summary> Creates one collection expression node. </summary>
    public CollectionExpression(Token leftBracket, SeparatedSyntaxList<Expression> expressions, Token rightBracket, IReadOnlyList<CollectionExpressionModifier> modifiers)
    {
        LeftBracket = leftBracket;
        Expressions = expressions;
        RightBracket = rightBracket;
        Modifiers = modifiers;
    }
}
