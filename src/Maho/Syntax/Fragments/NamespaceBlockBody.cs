using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Namespace body represented as a braced block. </summary>
internal sealed class NamespaceBlockBody : NamespaceBody
{
    /// <summary> Opening brace token. </summary>
    public Token OpenBrace { get; }
    /// <summary> Top-level members contained in the namespace block. </summary>
    public IReadOnlyList<TopLevel> Members { get; }
    /// <summary> Closing brace token. </summary>
    public Token CloseBrace { get; }

    /// <summary> Creates one namespace-block body node. </summary>
    public NamespaceBlockBody(Token openBrace, IReadOnlyList<TopLevel> members, Token closeBrace)
    {
        OpenBrace = openBrace;
        Members = members;
        CloseBrace = closeBrace;
    }

}
