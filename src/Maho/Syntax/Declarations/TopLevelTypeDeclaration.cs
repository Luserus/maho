namespace Maho.Syntax;

internal sealed class TopLevelTypeDeclaration : TopLevel
{
    public TypeDeclaration Type { get; }

    public TopLevelTypeDeclaration(TypeDeclaration type) => Type = type;
}