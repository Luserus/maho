namespace Maho.Syntax;

/// <summary> Local macro invocation declaration within a block or statement body (e.g. '$swap(a, b);'). </summary>
internal sealed class LocalMacroInvocationDeclaration : LocalDeclaration
{
    /// <summary> The macro invocation expression. </summary>
    public MacroInvocationExpression Invocation { get; }

    /// <summary> The optional terminating semicolon token. </summary>
    public Token? Semicolon { get; }

    public LocalMacroInvocationDeclaration(MacroInvocationExpression invocation, Token? semicolon)
    {
        Invocation = invocation;
        Semicolon = semicolon;
    }
}
