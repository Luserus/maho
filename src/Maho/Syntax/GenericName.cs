namespace Maho.Syntax;

internal sealed class GenericName : NamedSyntax
{
    public Token LessThanToken { get; }
    public SeparatedSyntaxList<NamedSyntax> TypeArguments { get; }
    public Token GreaterThanToken { get; }

    public GenericName(Token identifier, Token lessThanToken, SeparatedSyntaxList<NamedSyntax> typeArguments, Token greaterThanToken) : base(identifier)
    {
        LessThanToken = lessThanToken;
        TypeArguments = typeArguments;
        GreaterThanToken = greaterThanToken;
    }
}