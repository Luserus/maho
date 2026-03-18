namespace Maho.Syntax;

internal sealed class LocalTypeDeclaration : Local
{
    public TypeDeclaration Type { get; }

    public LocalTypeDeclaration(TypeDeclaration type) => Type = type;
}