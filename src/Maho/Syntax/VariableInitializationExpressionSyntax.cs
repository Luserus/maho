namespace Maho.Syntax;

/// <summary> Represents a variable initialization expression node. </summary>
internal sealed class VariableInitializationExpressionSyntax : ExpressionSyntax
{
    /// <summary> The type of the variable. </summary>
    public Token Type { get; }
    /// <summary> The name of the variable. </summary>
    public Token Identifier { get; }
    /// <summary> The assignment operator. </summary>
    public Token EqualsOp { get; }
    /// <summary> The initializer expression. </summary>
    public ExpressionSyntax Initializer { get; }

    /// <summary> Initializes the VariableInitializationExpressionSyntax class. </summary>
    /// <param name="type"> The type of the variable. </param>
    /// <param name="identifier"> The name of the variable. </param>
    /// <param name="equalsOp"> The assignment operator. </param>
    /// <param name="initializer"> The initializer expression. </param>
    public VariableInitializationExpressionSyntax(Token type, Token identifier, Token equalsOp, ExpressionSyntax initializer)
    {
        Type = type;
        Identifier = identifier;
        EqualsOp = equalsOp;
        Initializer = initializer;
    }
}