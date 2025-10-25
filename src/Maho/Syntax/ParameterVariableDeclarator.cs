namespace Maho.Syntax;

internal readonly struct ParameterVariableDeclarator
{
    public ModifierList Modifiers { get; }
    public NamedSyntax Type { get; }
    public IdentifierName Identifier { get; }

    public ParameterVariableDeclarator(ModifierList modifiers, NamedSyntax type, IdentifierName identifier)
    {
        Modifiers = modifiers;
        Type = type;
        Identifier = identifier;
    }
}