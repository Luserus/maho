namespace Maho.Syntax;

/// <summary> Type syntax rooted by a module or <c>global</c> qualifier using <c>::</c> (e.g., <c>ModA::Ns.Type</c> or <c>global::Type</c>). </summary>
internal sealed class ScopeQualifiedType : TypeSyntax
{
    /// <summary> Module identifier or <c>global</c> keyword token. </summary>
    public Token Qualifier { get; }

    /// <summary> Scope resolution operator (<c>::</c>). </summary>
    public Token ColonColonToken { get; }

    /// <summary> Member type path resolved inside the target module. </summary>
    public TypeSyntax MemberType { get; }

    public ScopeQualifiedType(Token qualifier, Token colonColonToken, TypeSyntax memberType)
    {
        Qualifier = qualifier;
        ColonColonToken = colonColonToken;
        MemberType = memberType;
    }
}

