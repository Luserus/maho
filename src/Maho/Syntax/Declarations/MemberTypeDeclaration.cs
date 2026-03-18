namespace Maho.Syntax;

internal sealed class MemberTypeDeclaration : Member
{
    public TypeDeclaration Type { get; }

    public MemberTypeDeclaration(TypeDeclaration type) => Type = type;
}
