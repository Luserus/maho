namespace Maho.Syntax;

internal sealed class GenericName : NamedSyntax
{
    public Token LessThanToken { get; }
    public ISeparatedSyntaxList TypeArguments { get; }
    public Token GreaterThanToken { get; }

    public GenericName(Token identifier, Token lessThanToken, ISeparatedSyntaxList typeArguments, Token greaterThanToken) : base(identifier)
    {
        LessThanToken = lessThanToken;
        TypeArguments = typeArguments;
        GreaterThanToken = greaterThanToken;
    }
}