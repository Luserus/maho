namespace Maho.Syntax;

internal abstract class NamedSyntax : SyntaxNode
{
    public Token Name { get; }

    protected NamedSyntax(Token name) => Name = name;
}
