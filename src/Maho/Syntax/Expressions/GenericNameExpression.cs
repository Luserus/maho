namespace Maho.Syntax;

internal sealed class GenericNameExpression : NamedExpression
{
    public Token LessThanToken { get; }
    public SeparatedSyntaxList<TypeSyntax> TypeArguments { get; }
    public Token GreaterThanToken { get; }

    public GenericNameExpression(Token identifier, Token lessThanToken, SeparatedSyntaxList<TypeSyntax> typeArguments, Token greaterThanToken) : base(identifier)
    {
        LessThanToken = lessThanToken;
        TypeArguments = typeArguments;
        GreaterThanToken = greaterThanToken;
    }
}