namespace Maho.Syntax;

internal sealed class TypeBaseClause : SyntaxNode
{
    public Token Colon { get; }
    public SeparatedSyntaxList<TypeSyntax> BaseTypes { get; }

    public TypeBaseClause(Token colon, SeparatedSyntaxList<TypeSyntax> baseTypes)
    {
        Colon = colon;
        BaseTypes = baseTypes;
    }
}
