namespace Maho.Syntax;

internal sealed class TypeBaseClause : SyntaxNode
{
    public Token Colon { get; }
    public SeparatedSyntaxList<BaseTypeItemSyntax> BaseTypes { get; }

    public TypeBaseClause(Token colon, SeparatedSyntaxList<BaseTypeItemSyntax> baseTypes)
    {
        Colon = colon;
        BaseTypes = baseTypes;
    }
}