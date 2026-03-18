namespace Maho.Syntax;

internal sealed class ModifiedTypeSyntax : TypeSyntax
{
    public TypeSyntax Type { get; }
    public PostfixTypeModifier? Modifier { get; }

    public ModifiedTypeSyntax(TypeSyntax type, PostfixTypeModifier? modifier)
    {
        Type = type;
        Modifier = modifier;
    }
}
