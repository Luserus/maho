namespace Maho.Syntax;

internal sealed class GenericName : NamedSyntax
{
    public Token Name { get; }
    public Token LessThanToken { get; }
    public SeparatedSyntaxList<SimpleName> TypeParameters { get; }
    public Token GreaterThanToken { get; }

    public GenericName(Token name, Token lessThanToken, SeparatedSyntaxList<SimpleName> typeParameters, Token greaterThanToken)
    {
        Name = name;
        LessThanToken = lessThanToken;
        TypeParameters = typeParameters;
        GreaterThanToken = greaterThanToken;
    }
}