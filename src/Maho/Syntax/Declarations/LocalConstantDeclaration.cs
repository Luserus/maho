namespace Maho.Syntax;

/// <summary>
/// Local constant declaration statement inside a function or method.
/// </summary>
internal sealed class LocalConstantDeclaration : LocalDeclaration
{
    public ConstantDeclaration Declaration { get; }
    public Token Semicolon { get; }

    public LocalConstantDeclaration(ConstantDeclaration declaration, Token semicolon)
    {
        Declaration = declaration;
        Semicolon = semicolon;
    }
}
