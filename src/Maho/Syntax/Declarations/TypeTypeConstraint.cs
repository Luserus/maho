namespace Maho.Syntax;

internal sealed class TypeTypeConstraint : TypeConstraint
{
    public TypeSyntax Type { get; }

    public TypeTypeConstraint(TypeSyntax type) => Type = type;
}