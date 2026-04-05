namespace Maho.Syntax;

/// <summary> Type syntax that carries an explicit generic type-argument list. </summary>
internal sealed class GenericType : TypeSyntax
{
    /// <summary> Base type name being specialized. </summary>
    public Token Name { get; }
    /// <summary> Opening angle bracket token. </summary>
    public Token LessThanToken { get; }
    /// <summary> Type arguments in source order. </summary>
    public SeparatedSyntaxList<TypeSyntax> TypeArguments { get; }
    /// <summary> Closing angle bracket token. </summary>
    public Token GreaterThanToken { get; }

    /// <summary> Creates one generic type node. </summary>
    public GenericType(Token name, Token lessThanToken, SeparatedSyntaxList<TypeSyntax> typeArguments, Token greaterThanToken)
    {
        Name = name;
        LessThanToken = lessThanToken;
        TypeArguments = typeArguments;
        GreaterThanToken = greaterThanToken;
    }
}
