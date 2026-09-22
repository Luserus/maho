namespace Maho.Syntax;

internal sealed class TypeConstraintClause : SyntaxNode
{
    public Token Keyword { get; }
    public SimpleName GenericParameter { get; }
    public Token Colon { get; }
    public SeparatedSyntaxList<TypeConstraint> Constraints { get; }

    public TypeConstraintClause(Token keyword, SimpleName genericParameter, Token colon, SeparatedSyntaxList<TypeConstraint> constraints)
    {
        Keyword = keyword;
        GenericParameter = genericParameter;
        Colon = colon;
        Constraints = constraints;
    }
}