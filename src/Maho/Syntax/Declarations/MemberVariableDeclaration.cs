namespace Maho.Syntax;

internal sealed class MemberFieldDeclaration : Member
{
    public VariableDeclaration Declaration { get; }
    public Token Semicolon { get; }

    public MemberFieldDeclaration(VariableDeclaration declaration, Token semicolon)
    {
        Declaration = declaration;
        Semicolon = semicolon;
    }
}