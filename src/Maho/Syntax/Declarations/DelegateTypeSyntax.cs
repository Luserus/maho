namespace Maho.Syntax;

/// <summary>
/// Syntactic representation of a delegate type: delegate(P1, P2, ...) -> ReturnType.
/// Aliases Std.Delegate&lt;TReturn, TParams...&gt;.
/// </summary>
internal sealed class DelegateTypeSyntax : TypeSyntax
{
    public Token DelegateKeyword { get; }
    public Token OpenParen { get; }
    public SeparatedSyntaxList<TypeSyntax> ParameterTypes { get; }
    public Token CloseParen { get; }
    public Token ArrowToken { get; }
    public TypeSyntax ReturnType { get; }

    public DelegateTypeSyntax(
        Token delegateKeyword,
        Token openParen,
        SeparatedSyntaxList<TypeSyntax> parameterTypes,
        Token closeParen,
        Token arrowToken,
        TypeSyntax returnType)
    {
        DelegateKeyword = delegateKeyword;
        OpenParen = openParen;
        ParameterTypes = parameterTypes;
        CloseParen = closeParen;
        ArrowToken = arrowToken;
        ReturnType = returnType;
    }
}
