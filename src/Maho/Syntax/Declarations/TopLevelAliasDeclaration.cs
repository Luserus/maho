namespace Maho.Syntax;

/// <summary>Wraps an alias declaration that appears in a top-level or namespace scope.</summary>
internal sealed class TopLevelAliasDeclaration : TopLevelDeclaration
{
    public AliasDeclaration Alias { get; }

    public TopLevelAliasDeclaration(AliasDeclaration alias) => Alias = alias;
}