namespace Maho.Syntax;

/// <summary> Represents a single arm of a macro definition: '(pattern) => template;' or '{ template }'. </summary>
internal sealed class MacroArmSyntax : SyntaxNode
{
    /// <summary> The parameter pattern matched by this arm, or null if parameterless. </summary>
    public MacroPatternSyntax? Pattern { get; }

    /// <summary> The '=>' arrow token, or null if shorthand block body. </summary>
    public Token? ArrowToken { get; }

    /// <summary> The expansion template for this arm. </summary>
    public MacroTemplateSyntax Template { get; }

    /// <summary> The terminating semicolon token if present. </summary>
    public Token? Semicolon { get; }

    public MacroArmSyntax(MacroPatternSyntax? pattern, Token? arrowToken, MacroTemplateSyntax template, Token? semicolon)
    {
        Pattern = pattern;
        ArrowToken = arrowToken;
        Template = template;
        Semicolon = semicolon;
    }
}
