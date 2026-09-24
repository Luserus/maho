namespace Maho.Syntax;

/// <summary> Represents a single parameter within a macro pattern, such as '@item: expr' or '@rest: expr...'. </summary>
internal sealed class MacroPatternParameter : SyntaxNode
{
    /// <summary> The '@' symbol token if this is a named parameter. </summary>
    public Token? AtToken { get; }

    /// <summary> The parameter name token (or literal token if constant literal match). </summary>
    public Token Name { get; }

    /// <summary> The colon separator token between parameter name and kind classifier. </summary>
    public Token? ColonToken { get; }

    /// <summary> The syntactic category of this parameter. </summary>
    public MacroParameterKind Kind { get; }

    /// <summary> Indicates whether this parameter matches a variadic pack ('...'). </summary>
    public bool IsVariadic { get; }

    /// <summary> The constant literal expression if this is a literal pattern (e.g. 0). </summary>
    public Expression? LiteralExpression { get; }

    public MacroPatternParameter(
        Token? atToken,
        Token name,
        Token? colonToken,
        MacroParameterKind kind,
        bool isVariadic,
        Expression? literalExpression = null)
    {
        AtToken = atToken;
        Name = name;
        ColonToken = colonToken;
        Kind = kind;
        IsVariadic = isVariadic;
        LiteralExpression = literalExpression;
    }
}
