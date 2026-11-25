namespace Maho.Syntax;

/// <summary> Expression accessing a symbol via module or <c>global::</c> scope resolution (<c>ModA::Symbol</c> or <c>global::Symbol</c>). </summary>
internal sealed class ScopeResolutionExpression : Expression
{
    /// <summary> Module identifier or <c>global</c> keyword token. </summary>
    public Token Qualifier { get; }

    /// <summary> Scope resolution operator (<c>::</c>). </summary>
    public Token ColonColonToken { get; }

    /// <summary> Member name expression resolved within the target module. </summary>
    public NamedExpression Member { get; }

    public ScopeResolutionExpression(Token qualifier, Token colonColonToken, NamedExpression member)
    {
        Qualifier = qualifier;
        ColonColonToken = colonColonToken;
        Member = member;
    }
}

