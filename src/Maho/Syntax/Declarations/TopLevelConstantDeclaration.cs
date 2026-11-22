namespace Maho.Syntax;

/// <summary>
/// Top-level constant declaration statement.
/// </summary>
internal sealed class TopLevelConstantDeclaration : TopLevelDeclaration
{
    public ConstantDeclaration Declaration { get; }
    public Token Semicolon { get; }

    public TopLevelConstantDeclaration(ConstantDeclaration declaration, Token semicolon)
    {
        Declaration = declaration;
        Semicolon = semicolon;
    }
}
