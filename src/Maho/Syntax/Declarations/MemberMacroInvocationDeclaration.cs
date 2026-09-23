namespace Maho.Syntax;

/// <summary> Member macro invocation declaration within a type body (e.g. '$Point2D;' or '$AddFields(x);'). </summary>
internal sealed class MemberMacroInvocationDeclaration : Member
{
    /// <summary> The macro invocation expression. </summary>
    public MacroInvocationExpression Invocation { get; }

    /// <summary> The optional terminating semicolon token. </summary>
    public Token? Semicolon { get; }

    public MemberMacroInvocationDeclaration(MacroInvocationExpression invocation, Token? semicolon)
    {
        Invocation = invocation;
        Semicolon = semicolon;
    }
}
