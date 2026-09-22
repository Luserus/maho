namespace Maho.Syntax;

/// <summary> Local declaration introducing a type alias within a function or statement block. </summary>
internal sealed class LocalAliasDeclaration : LocalDeclaration
{
    /// <summary> The wrapped alias declaration. </summary>
    public AliasDeclaration Alias { get; }

    /// <summary> Creates one local alias declaration node. </summary>
    public LocalAliasDeclaration(AliasDeclaration alias) => Alias = alias;
}
