namespace Maho.Syntax;

/// <summary> Pattern matching a literal value, e.g. <c>1</c>, <c>"abc"</c>. </summary>
internal sealed class LiteralPattern : PatternSyntax
{
    public LiteralExpression Literal { get; }

    public LiteralPattern(LiteralExpression literal)
    {
        Literal = literal;
    }
}

