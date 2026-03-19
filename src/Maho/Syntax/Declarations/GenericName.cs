namespace Maho.Syntax;

internal sealed class GenericName : NamedSyntax
{
    public Token LessThanToken { get; }
    public SeparatedSyntaxList<SimpleName> TypeParameters { get; }
    public Token GreaterThanToken { get; }

    public GenericName(Token name, Token lessThanToken, SeparatedSyntaxList<SimpleName> typeParameters, Token greaterThanToken) : base(name)
    {
        LessThanToken = lessThanToken;
        TypeParameters = typeParameters;
        GreaterThanToken = greaterThanToken;
    }
}