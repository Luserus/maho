namespace Maho.Syntax;

/// <summary> Name syntax rooted by a module or <c>global</c> qualifier using <c>::</c>. </summary>
internal sealed class ScopeQualifiedName : NamedSyntax
{
    /// <summary> Module identifier or <c>global</c> keyword token. </summary>
    public Token Qualifier { get; }

    /// <summary> Scope resolution operator (<c>::</c>). </summary>
    public Token ColonColonToken { get; }

    /// <summary> Member name resolved inside the target module. </summary>
    public NamedSyntax MemberName { get; }

    public ScopeQualifiedName(Token qualifier, Token colonColonToken, NamedSyntax memberName)
    {
        Qualifier = qualifier;
        ColonColonToken = colonColonToken;
        MemberName = memberName;
    }
}

