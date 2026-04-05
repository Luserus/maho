namespace Maho.Syntax;

/// <summary> Represents a variable declaration statement node. </summary>
internal sealed class LocalVariableDeclarationStatement : LocalStatement
{
    /// <summary> The variable declaration. </summary>
    public VariableDeclaration Declaration { get; }
    /// <summary> The statement terminator. </summary>
    public Token Semicolon { get; }

    /// <summary> Creates one local variable declaration statement node. </summary>
    /// <param name="declaration"> The variable declaration. </param>
    /// <param name="semicolon"> The statement terminator. </param>
    public LocalVariableDeclarationStatement(VariableDeclaration declaration, Token semicolon)
    {
        Declaration = declaration;
        Semicolon = semicolon;
    }
}
