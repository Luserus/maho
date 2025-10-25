namespace Maho.Syntax;

internal abstract class NamedSyntax : ISyntaxNode
{
    public Token Identifier { get; }

    protected NamedSyntax(Token identifier)
    {
        Identifier = identifier;
    }
}