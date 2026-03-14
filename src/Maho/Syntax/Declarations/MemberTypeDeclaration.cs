namespace Maho.Syntax;

internal sealed class MemberTypeDeclaration : Member
{
    public TypeSyntax Type { get; }

    public MemberTypeDeclaration(TypeSyntax type) => Type = type;
}
