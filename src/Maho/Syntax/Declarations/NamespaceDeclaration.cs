namespace Maho.Syntax;

/// <summary> Namespace declaration with a qualified name and a body form. </summary>
internal sealed class NamespaceDeclaration : TopLevel
{
    /// <summary> Namespace keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Declared namespace name. </summary>
    public NamedSyntax Name { get; }
    /// <summary> Body or terminator for the declaration. </summary>
    public NamespaceBody Body { get; }

    /// <summary> Creates one namespace declaration node. </summary>
    public NamespaceDeclaration(Token keyword, NamedSyntax name, NamespaceBody body)
    {
        Keyword = keyword;
        Name = name;
        Body = body;
    }
}
