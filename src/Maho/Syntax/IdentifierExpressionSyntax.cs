namespace Maho.Syntax;

/// <summary> Represents an identifier expression node. </summary>
internal sealed class IdentifierExpressionSyntax : ExpressionSyntax
{
    /// <summary> Initializes the IdentifierExpressionSyntax class. </summary>
    /// <param name="identifier"> The identifier token. </param>
    public IdentifierExpressionSyntax(Token identifier)
    {
        Identifier = identifier;
    }

    /// <summary> The identifier token. </summary>
    public Token Identifier { get; }
}