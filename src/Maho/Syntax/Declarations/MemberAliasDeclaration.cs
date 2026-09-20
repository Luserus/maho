namespace Maho.Syntax;

/// <summary> Member declaration introducing a type alias within a type or member block. </summary>
internal sealed class MemberAliasDeclaration : Member
{
    /// <summary> The wrapped alias declaration. </summary>
    public AliasDeclaration Alias { get; }

    /// <summary> Creates one member alias declaration node. </summary>
    public MemberAliasDeclaration(AliasDeclaration alias) => Alias = alias;
}
