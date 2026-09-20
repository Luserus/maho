namespace Maho.Syntax;

/// <summary>
/// A bare name supplied as a generic argument. Its interpretation is deferred until the generic target's
/// parameter kind is known: it may denote either a type or a compile-time expression.
/// </summary>
internal sealed class NamedExpressionGenericArgument : TypeSyntax
{
    /// <summary>The bare name in the generic argument list.</summary>
    public IdentifierNameExpression Expression { get; }

    /// <summary>Creates one deferred named generic argument.</summary>
    public NamedExpressionGenericArgument(IdentifierNameExpression expression) => Expression = expression;
}