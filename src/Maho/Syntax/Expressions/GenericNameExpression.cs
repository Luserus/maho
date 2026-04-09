namespace Maho.Syntax;

/// <summary> Expression form of a generic name invocation. </summary>
internal sealed class GenericNameExpression : NamedExpression
{
    /// <summary> Opening angle bracket token. </summary>
    public Token LessThanToken { get; }
    /// <summary> Generic type arguments. </summary>
    public SeparatedSyntaxList<TypeSyntax> TypeArguments { get; }
    /// <summary> Closing angle bracket token. </summary>
    public Token GreaterThanToken { get; }

    /// <summary> Creates one generic-name expression node. </summary>
    public GenericNameExpression(Token identifier, Token lessThanToken, SeparatedSyntaxList<TypeSyntax> typeArguments, Token greaterThanToken) : base(identifier)
    {
        LessThanToken = lessThanToken;
        TypeArguments = typeArguments;
        GreaterThanToken = greaterThanToken;
    }
}
