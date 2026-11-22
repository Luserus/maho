namespace Maho.Syntax;

/// <summary>
/// Represents a multi-term element inside a collection expression (e.g. <c>k : v</c> or <c>a : b : c</c>).
/// </summary>
internal sealed class MultiTermExpression : Expression
{
    /// <summary> The terms comprising this element. </summary>
    public SeparatedSyntaxList<Expression> Terms { get; }

    public MultiTermExpression(SeparatedSyntaxList<Expression> terms)
    {
        Terms = terms;
    }
}
