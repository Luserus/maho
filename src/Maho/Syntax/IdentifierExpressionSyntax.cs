namespace Maho.Syntax;

/// <summary> Represents an identifier expression node. </summary>
/// <param name="identifer"> The identifier token. </param>
internal sealed class IdentifierExpressionSyntax(Token identifer) : ExpressionSyntax
{
    /// <summary> The identifier token. </summary>
    public Token Identifier { get; } = identifer;
}