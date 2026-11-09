namespace Maho.Syntax;

/// <summary> Pattern matching a field inside a struct variant pattern, e.g. <c>x</c> or <c>x: pattern</c>. </summary>
internal sealed class FieldPatternSyntax : SyntaxNode
{
    public NamedSyntax FieldName { get; }
    public Token? ColonToken { get; }
    public PatternSyntax Pattern { get; }

    public FieldPatternSyntax(NamedSyntax fieldName, Token? colonToken, PatternSyntax pattern)
    {
        FieldName = fieldName;
        ColonToken = colonToken;
        Pattern = pattern;
    }
}

