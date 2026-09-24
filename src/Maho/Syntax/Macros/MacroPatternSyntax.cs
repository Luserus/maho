using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Represents a pattern matching arm's parameter list: '(@a: expr, @b: type)'. </summary>
internal sealed class MacroPatternSyntax : SyntaxNode
{
    /// <summary> The opening parenthesis token '('. </summary>
    public Token? LeftParen { get; }

    /// <summary> The parameters in this pattern. </summary>
    public IReadOnlyList<MacroPatternParameter> Parameters { get; }

    /// <summary> The closing parenthesis token ')'. </summary>
    public Token? RightParen { get; }

    public MacroPatternSyntax(Token? leftParen, IReadOnlyList<MacroPatternParameter> parameters, Token? rightParen)
    {
        LeftParen = leftParen;
        Parameters = parameters;
        RightParen = rightParen;
    }
}
