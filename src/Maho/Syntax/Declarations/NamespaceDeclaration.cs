namespace Maho.Syntax;

internal sealed class NamespaceDeclaration : TopLevel
{
    public Token Keyword { get; }
    public NamedSyntax Name { get; }
    public NamespaceBody Body { get; }

    public NamespaceDeclaration(Token keyword, NamedSyntax name, NamespaceBody body)
    {
        Keyword = keyword;
        Name = name;
        Body = body;
    }
}