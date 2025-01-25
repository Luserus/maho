namespace Maho.Syntax;

/// <summary> Represents a variable declaration statement node. </summary>
internal sealed class VariableDeclarationStatementSyntax : StatementSyntax
{
    /// <summary> The type of the variable. </summary>
    public Token Type { get; }
    /// <summary> The name of the variable. </summary>
    public Token Identifier { get; }
    /// <summary> The statement terminator. </summary>
    public Token Semicolon { get; }

    /// <summary> Initializes the VariableDeclarationStatementSyntax class. </summary>
    /// <param name="type"> The type of the variable. </param>
    /// <param name="identifier"> The name of the variable. </param>
    /// <param name="semicolon"> The statement terminator. </param>
    public VariableDeclarationStatementSyntax(Token type, Token identifier, Token semicolon)
    {
        Type = type;
        Identifier = identifier;
        Semicolon = semicolon;
    }
}