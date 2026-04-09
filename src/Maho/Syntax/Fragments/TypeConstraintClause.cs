namespace Maho.Syntax;

internal sealed class TypeConstraintClause : SyntaxNode
{
    public Token Keyword { get; }
    public SimpleName TypeParameter { get; }
    public Token Colon { get; }
    public SeparatedSyntaxList<TypeConstraint> Constraints { get; }

    public TypeConstraintClause(Token keyword, SimpleName typeParameter, Token colon, SeparatedSyntaxList<TypeConstraint> constraints)
    {
        Keyword = keyword;
        TypeParameter = typeParameter;
        Colon = colon;
        Constraints = constraints;
    }
}