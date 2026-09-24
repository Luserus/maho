using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Represents a single argument passed to a macro invocation. </summary>
internal sealed class MacroArgumentSyntax : SyntaxNode
{
    /// <summary> The token slice comprising the argument. </summary>
    public IReadOnlyList<Token> Tokens { get; }

    /// <summary> The pre-parsed expression representation if available. </summary>
    public Expression? ParsedExpression { get; }

    public MacroArgumentSyntax(IReadOnlyList<Token> tokens, Expression? parsedExpression = null)
    {
        Tokens = tokens;
        ParsedExpression = parsedExpression;
    }
}
