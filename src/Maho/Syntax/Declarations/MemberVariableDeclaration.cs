namespace Maho.Syntax;

/// <summary> Member declaration that introduces one or more fields. </summary>
internal sealed class MemberFieldDeclaration : Member
{
    /// <summary> Variable declaration being introduced. </summary>
    public VariableDeclaration Declaration { get; }
    /// <summary> Statement terminator for the declaration. </summary>
    public Token Semicolon { get; }

    /// <summary> Creates one member-field declaration node. </summary>
    public MemberFieldDeclaration(VariableDeclaration declaration, Token semicolon)
    {
        Declaration = declaration;
        Semicolon = semicolon;
    }
}
