namespace Maho.Syntax;

internal sealed class TopLevelTypeDeclaration : TopLevel
{
    public TypeSyntax Type { get; }

    public TopLevelTypeDeclaration(TypeSyntax type) => Type = type;
}