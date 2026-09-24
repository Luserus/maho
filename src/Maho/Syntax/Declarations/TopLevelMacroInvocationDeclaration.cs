namespace Maho.Syntax;

/// <summary> Top-level macro invocation declaration (e.g. '$SomeMacro(args);' or '$SomeMixin;'). </summary>
internal sealed class TopLevelMacroInvocationDeclaration : TopLevelDeclaration
{
    /// <summary> The macro invocation expression. </summary>
    public MacroInvocationExpression Invocation { get; }

    /// <summary> The optional terminating semicolon token. </summary>
    public Token? Semicolon { get; }

    public TopLevelMacroInvocationDeclaration(MacroInvocationExpression invocation, Token? semicolon)
    {
        Invocation = invocation;
        Semicolon = semicolon;
    }
}
