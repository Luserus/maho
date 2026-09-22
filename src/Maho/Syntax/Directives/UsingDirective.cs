namespace Maho.Syntax;

/// <summary>Declares a using directive that imports a namespace into the current scope.</summary>
internal sealed class UsingDirective : Directive
{
    public Token Keyword { get; }
    public NamedSyntax Namespace { get; }
    public Token Semicolon { get; }

    public UsingDirective(Token keyword, NamedSyntax @namespace, Token semicolon)
    {
        Keyword = keyword;
        Namespace = @namespace;
        Semicolon = semicolon;
    }
}
