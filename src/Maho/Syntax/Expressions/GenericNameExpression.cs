namespace Maho.Syntax;

/// <summary> Expression form of a generic name invocation. </summary>
internal sealed class GenericNameExpression : NamedExpression
{
    /// <summary> Optional colon-colon token for turbofish explicit disambiguation (::). </summary>
    public Token? ColonColonToken { get; }
    /// <summary> Opening angle bracket token. </summary>
    public Token LessThanToken { get; }
    /// <summary> Generic arguments. </summary>
    public SeparatedSyntaxList<TypeSyntax> GenericArguments { get; }
    /// <summary> Closing angle bracket token. </summary>
    public Token GreaterThanToken { get; }

    /// <summary> Creates one generic-name expression node without a turbofish token. </summary>
    public GenericNameExpression(Token identifier, Token lessThanToken, SeparatedSyntaxList<TypeSyntax> genericArguments, Token greaterThanToken)
        : this(identifier, null, lessThanToken, genericArguments, greaterThanToken)
    {
    }

    /// <summary> Creates one generic-name expression node with an optional turbofish token. </summary>
    public GenericNameExpression(Token identifier, Token? colonColonToken, Token lessThanToken, SeparatedSyntaxList<TypeSyntax> genericArguments, Token greaterThanToken)
        : base(identifier)
    {
        ColonColonToken = colonColonToken;
        LessThanToken = lessThanToken;
        GenericArguments = genericArguments;
        GreaterThanToken = greaterThanToken;
    }
}