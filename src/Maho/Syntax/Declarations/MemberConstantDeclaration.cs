namespace Maho.Syntax;

/// <summary>
/// Member declaration introducing a constant within a type.
/// </summary>
internal sealed class MemberConstantDeclaration : Member
{
    public ConstantDeclaration Declaration { get; }
    public Token Semicolon { get; }

    public MemberConstantDeclaration(ConstantDeclaration declaration, Token semicolon)
    {
        Declaration = declaration;
        Semicolon = semicolon;
    }
}
