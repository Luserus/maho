namespace Maho.Syntax;

/// <summary>
/// Syntactic representation of a function pointer type: func(P1, P2, ...) -> ReturnType.
/// Aliases Std.Function&lt;TReturn, TParams...&gt;.
/// </summary>
internal sealed class FunctionTypeSyntax : TypeSyntax
{
    public Token FuncKeyword { get; }
    public Token OpenParen { get; }
    public SeparatedSyntaxList<TypeSyntax> ParameterTypes { get; }
    public Token CloseParen { get; }
    public Token ArrowToken { get; }
    public TypeSyntax ReturnType { get; }

    public FunctionTypeSyntax(
        Token funcKeyword,
        Token openParen,
        SeparatedSyntaxList<TypeSyntax> parameterTypes,
        Token closeParen,
        Token arrowToken,
        TypeSyntax returnType)
    {
        FuncKeyword = funcKeyword;
        OpenParen = openParen;
        ParameterTypes = parameterTypes;
        CloseParen = closeParen;
        ArrowToken = arrowToken;
        ReturnType = returnType;
    }
}
