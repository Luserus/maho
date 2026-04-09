namespace Maho.Syntax;

/// <summary> Type body for enum-like declarations that store a separated member list. </summary>
internal sealed class TypeEnumBody : TypeBody
{
    /// <summary> Enum members in source order. </summary>
    public SeparatedSyntaxList<SyntaxNode> Members { get; }

    /// <summary> Creates one enum-body node. </summary>
    public TypeEnumBody(SeparatedSyntaxList<SyntaxNode> members) => Members = members;
}
