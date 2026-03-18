namespace Maho.Syntax;

internal sealed class IdentifierTypeSyntax : TypeSyntax
{
    public Token Name { get; }

    public IdentifierTypeSyntax(Token name)
    {
        Name = name;
    }
}
