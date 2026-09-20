namespace Maho.Syntax;

/// <summary> Type syntax that carries an explicit generic-argument list. </summary>
internal sealed class GenericType : TypeSyntax
{
    /// <summary> Base type name being specialized. </summary>
    public Token Name { get; }
    /// <summary> Opening angle bracket token. </summary>
    public Token LessThanToken { get; }
    /// <summary> Generic arguments in source order. </summary>
    public SeparatedSyntaxList<TypeSyntax> GenericArguments { get; }
    /// <summary> Closing angle bracket token. </summary>
    public Token GreaterThanToken { get; }

    /// <summary> Creates one generic type node. </summary>
    public GenericType(Token name, Token lessThanToken, SeparatedSyntaxList<TypeSyntax> genericArguments, Token greaterThanToken)
    {
        Name = name;
        LessThanToken = lessThanToken;
        GenericArguments = genericArguments;
        GreaterThanToken = greaterThanToken;
    }
}