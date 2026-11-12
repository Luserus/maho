namespace Maho.Syntax;

/// <summary> In-place struct variant creation expression, e.g. <c>E.C { x = val }</c>. </summary>
internal sealed class VariantCreationExpression : Expression
{
    public Expression Target { get; }
    public TypeInitializer Initializer { get; }

    public VariantCreationExpression(Expression target, TypeInitializer initializer)
    {
        Target = target;
        Initializer = initializer;
    }
}

