using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Represents the template body of a macro arm, preserving token structure for hygiene and substitution. </summary>
internal sealed class MacroTemplateSyntax : SyntaxNode
{
    /// <summary> The token stream comprising the macro template. </summary>
    public IReadOnlyList<Token> Tokens { get; }

    /// <summary> True if defined with an expression arrow '=> expr;' rather than a block '{ ... }'. </summary>
    public bool IsExpression { get; }

    public MacroTemplateSyntax(IReadOnlyList<Token> tokens, bool isExpression)
    {
        Tokens = tokens;
        IsExpression = isExpression;
    }
}
