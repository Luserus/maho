namespace Maho.Syntax;

internal sealed class LocalTypeDeclaration : Local
{
    public TypeSyntax Type { get; }

    public LocalTypeDeclaration(TypeSyntax type) => Type = type;
}