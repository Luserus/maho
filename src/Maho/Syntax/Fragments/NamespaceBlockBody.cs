using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Namespace body represented as a braced block. </summary>
internal sealed class NamespaceBlockBody : NamespaceBody
{
    /// <summary> Opening brace token. </summary>
    public Token OpenBrace { get; }
    /// <summary> Directives declared within this namespace block. </summary>
    public IReadOnlyList<Directive> Directives { get; }
    /// <summary> Using directives declared within this namespace block. </summary>
    public IReadOnlyList<UsingDirective> Usings { get; }
    /// <summary> Top-level members contained in the namespace block. </summary>
    public IReadOnlyList<TopLevel> Members { get; internal set; }
    /// <summary> Closing brace token. </summary>
    public Token CloseBrace { get; }

    /// <summary> Creates one namespace-block body node. </summary>
    public NamespaceBlockBody(Token openBrace, IReadOnlyList<Directive> directives, IReadOnlyList<TopLevel> members, Token closeBrace)
    {
        OpenBrace = openBrace;
        Directives = directives;
        var usings = new List<UsingDirective>();

        foreach (var directive in directives)
        {
            if (directive is UsingDirective @using)
                usings.Add(@using);
        }

        Usings = usings;
        Members = members;
        CloseBrace = closeBrace;
    }

    /// <summary> Backward-compatible constructor accepting only members. </summary>
    public NamespaceBlockBody(Token openBrace, IReadOnlyList<TopLevel> members, Token closeBrace)
        : this(openBrace, [], members, closeBrace)
    {
    }
}