namespace Maho.Syntax;

/// <summary> Member declaration that introduces a nested type. </summary>
internal sealed class MemberTypeDeclaration : Member
{
    /// <summary> Nested type declaration. </summary>
    public TypeDeclaration Type { get; }

    /// <summary> Creates one member-type declaration node. </summary>
    public MemberTypeDeclaration(TypeDeclaration type) => Type = type;
}
