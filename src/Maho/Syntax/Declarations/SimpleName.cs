namespace Maho.Syntax;

internal sealed class SimpleName : NamedSyntax
{
    public Token Name { get; }
    public SimpleName(Token name) => Name = name;
}
