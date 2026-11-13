using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary>
/// Syntactic representation of a lambda expression:
/// Forms:
///   x => expr
///   (x, y) => expr
///   (T x, T y) => expr
///   (T x, T y) -> int => expr
///   () -> T => expr
///   (x, y) => { return x + y; }
/// </summary>
internal sealed class LambdaExpression : Expression
{
    public IReadOnlyList<LambdaParameterSyntax> Parameters { get; }
    public Token? ArrowToken { get; }
    public TypeSyntax? ReturnType { get; }
    public Token EqualsGreaterThanToken { get; }
    public Expression Body { get; }

    public LambdaExpression(
        IReadOnlyList<LambdaParameterSyntax> parameters,
        Token? arrowToken,
        TypeSyntax? returnType,
        Token equalsGreaterThanToken,
        Expression body)
    {
        Parameters = parameters;
        ArrowToken = arrowToken;
        ReturnType = returnType;
        EqualsGreaterThanToken = equalsGreaterThanToken;
        Body = body;
    }
}
