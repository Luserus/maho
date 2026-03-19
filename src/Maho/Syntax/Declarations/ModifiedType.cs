namespace Maho.Syntax;

internal sealed class ModifiedType : TypeSyntax
{
    public TypeSyntax Type { get; }
    public PostfixTypeModifier? Modifier { get; }

    public ModifiedType(TypeSyntax type, PostfixTypeModifier? modifier)
    {
        Type = type;
        Modifier = modifier;
    }
}
