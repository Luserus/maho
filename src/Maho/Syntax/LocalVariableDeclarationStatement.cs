namespace Maho.Syntax;

/// <summary> Represents a variable declaration statement node. </summary>
internal sealed class LocalVariableDeclarationStatement : LocalStatement
{
    /// <summary> The type of the variable. </summary>
    public NamedSyntax Type { get; }
    public ISeparatedSyntaxList Declarators { get; }
    /// <summary> The statement terminator. </summary>
    public Token Semicolon { get; }

    /// <summary> Initializes the VariableDeclarationStatementSyntax class. </summary>
    /// <param name="type"> The type of the variable. </param>
    /// <param name="declarators"> The variable declarators. </param>
    /// <param name="semicolon"> The statement terminator. </param>
    public LocalVariableDeclarationStatement(NamedSyntax type, ISeparatedSyntaxList declarators, Token semicolon)
    {
        Type = type;
        Declarators = declarators;
        Semicolon = semicolon;
    }
}