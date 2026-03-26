namespace Maho.Syntax;

internal abstract class ObjectCreationExpression : Expression
{
    public Token Keyword { get; }
    public ObjectCreationKind Kind { get; }

    public ObjectCreationExpression(Token keyword, ObjectCreationKind kind)
    {
        Keyword = keyword;
        Kind = kind;
    }
}