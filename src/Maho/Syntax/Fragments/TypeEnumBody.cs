namespace Maho.Syntax;

// WIP
internal sealed class TypeEnumBody : TypeBody
{
    public SeparatedSyntaxList<SyntaxNode> Members { get; }

    public TypeEnumBody(SeparatedSyntaxList<SyntaxNode> members) => Members = members;
}