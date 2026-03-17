namespace Maho.Syntax;

internal sealed class TopLevelVariableDeclaration : TopLevel
{
    /// <summary> The variable declaration. </summary>
    public VariableDeclaration Declaration { get; }
    /// <summary> The statement terminator. </summary>
    public Token Semicolon { get; }

    /// <summary> Initializes the TopLevelVariableDeclarationStatementSyntax class. </summary>
    /// <param name="declaration"> The variable declaration. </param>
    /// <param name="semicolon"> The statement terminator. </param>
    public TopLevelVariableDeclaration(VariableDeclaration declaration, Token semicolon)
    {
        Declaration = declaration;
        Semicolon = semicolon;
    }
}