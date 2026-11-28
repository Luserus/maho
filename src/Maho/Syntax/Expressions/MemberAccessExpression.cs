namespace Maho.Syntax;

/// <summary> Member access expression using dot notation, with optional generic type arguments. </summary>
internal sealed class MemberAccessExpression : Expression
{
    /// <summary> Target expression being accessed. </summary>
    public Expression Expression { get; }
    /// <summary> Dot token. </summary>
    public Token Dot { get; }
    /// <summary> Member identifier token. </summary>
    public Token Identifier { get; }
    /// <summary> Optional turbofish token preceding generic arguments. </summary>
    public Token? ColonColonToken { get; }
    /// <summary> Optional opening angle bracket token for member generic arguments. </summary>
    public Token? LessThanToken { get; }
    /// <summary> Optional generic type arguments supplied on the member access. </summary>
    public SeparatedSyntaxList<TypeSyntax>? GenericArguments { get; }
    /// <summary> Optional closing angle bracket token for member generic arguments. </summary>
    public Token? GreaterThanToken { get; }

    /// <summary> Creates one member-access expression node. </summary>
    public MemberAccessExpression(
        Expression expression,
        Token dot,
        Token identifier,
        Token? colonColonToken = null,
        Token? lessThanToken = null,
        SeparatedSyntaxList<TypeSyntax>? genericArguments = null,
        Token? greaterThanToken = null)
    {
        Expression = expression;
        Dot = dot;
        Identifier = identifier;
        ColonColonToken = colonColonToken;
        LessThanToken = lessThanToken;
        GenericArguments = genericArguments;
        GreaterThanToken = greaterThanToken;
    }
}