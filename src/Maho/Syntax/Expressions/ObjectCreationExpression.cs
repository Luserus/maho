namespace Maho.Syntax;

internal sealed class ObjectCreationExpression : Expression
{
    public Token Keyword { get; }
    public ObjectCreationKind Kind { get; }
    public NamedSyntax Type { get; }
    public Token OpenParen { get; }
    public SeparatedSyntaxList<Expression> Arguments { get; }
    public Token CloseParen { get; }

    public ObjectCreationExpression(Token keyword, ObjectCreationKind kind, NamedSyntax type, Token openParen, SeparatedSyntaxList<Expression> arguments, Token closeParen)
    {
        Keyword = keyword;
        Kind = kind;
        Type = type;
        OpenParen = openParen;
        Arguments = arguments;
        CloseParen = closeParen;
    }
}