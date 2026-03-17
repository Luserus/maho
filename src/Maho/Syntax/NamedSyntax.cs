namespace Maho.Syntax;

internal abstract class NamedSyntax : SyntaxNode
{
    public Token Identifier { get; }

    protected NamedSyntax(Token identifier)
    {
        Identifier = identifier;
    }
}