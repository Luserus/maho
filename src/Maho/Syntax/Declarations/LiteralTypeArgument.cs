namespace Maho.Syntax;

/// <summary>A literal compile-time argument supplied to a generic type.</summary>
internal sealed class LiteralTypeArgument : TypeSyntax
{
    /// <summary>The literal token representing the compile-time value.</summary>
    public Token Literal { get; }

    /// <summary>Creates one literal generic argument.</summary>
    public LiteralTypeArgument(Token literal) => Literal = literal;
}
