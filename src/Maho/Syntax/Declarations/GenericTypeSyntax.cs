namespace Maho.Syntax;

internal sealed class GenericTypeSyntax : TypeSyntax
{
    public Token Name { get; }
    public Token LessThanToken { get; }
    public SeparatedSyntaxList<TypeSyntax> TypeArguments { get; }
    public Token GreaterThanToken { get; }

    public GenericTypeSyntax(Token name, Token lessThanToken, SeparatedSyntaxList<TypeSyntax> typeArguments, Token greaterThanToken)
    {
        Name = name;
        LessThanToken = lessThanToken;
        TypeArguments = typeArguments;
        GreaterThanToken = greaterThanToken;
    }
}