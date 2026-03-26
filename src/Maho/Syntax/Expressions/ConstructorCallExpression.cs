namespace Maho.Syntax;

internal sealed class ConstructorCallExpression : ObjectCreationExpression
{
    public TypeSyntax Type { get; }
    public Token OpenParen { get; }
    public SeparatedSyntaxList<Expression> Arguments { get; }
    public Token CloseParen { get; }

    public ConstructorCallExpression(Token keyword, ObjectCreationKind kind, TypeSyntax type, Token openParen, SeparatedSyntaxList<Expression> arguments, Token closeParen) : base(keyword, kind)
    {
        Type = type;
        OpenParen = openParen;
        Arguments = arguments;
        CloseParen = closeParen;
    }
}