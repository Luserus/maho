namespace Maho.Syntax;

/// <summary> Top-level variable declaration statement. </summary>
internal sealed class TopLevelVariableDeclaration : TopLevel
{
    /// <summary> The variable declaration. </summary>
    public VariableDeclaration Declaration { get; }
    /// <summary> The statement terminator. </summary>
    public Token Semicolon { get; }

    /// <summary> Creates one top-level variable declaration statement node. </summary>
    /// <param name="declaration"> The variable declaration. </param>
    /// <param name="semicolon"> The statement terminator. </param>
    public TopLevelVariableDeclaration(VariableDeclaration declaration, Token semicolon)
    {
        Declaration = declaration;
        Semicolon = semicolon;
    }
}
