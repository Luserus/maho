using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Represents a macro invocation expression, e.g. '$fact(5)' or parameterless '$here'. </summary>
internal sealed class MacroInvocationExpression : Expression
{
    /// <summary> The '$' symbol token prefixing the macro name. </summary>
    public Token DollarToken { get; }

    /// <summary> The macro identifier token. </summary>
    public Token Name { get; }

    /// <summary> The opening parenthesis token '(', or null if parameterless invocation. </summary>
    public Token? LeftParen { get; }

    /// <summary> The list of arguments supplied to the invocation. </summary>
    public IReadOnlyList<MacroArgumentSyntax> Arguments { get; }

    /// <summary> The closing parenthesis token ')', or null if parameterless invocation. </summary>
    public Token? RightParen { get; }

    public MacroInvocationExpression(
        Token dollarToken,
        Token name,
        Token? leftParen,
        IReadOnlyList<MacroArgumentSyntax> arguments,
        Token? rightParen)
    {
        DollarToken = dollarToken;
        Name = name;
        LeftParen = leftParen;
        Arguments = arguments;
        RightParen = rightParen;
    }
}
