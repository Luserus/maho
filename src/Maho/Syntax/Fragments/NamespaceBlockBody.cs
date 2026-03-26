using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class NamespaceBlockBody : NamespaceBody
{
    public Token OpenBrace { get; }
    public IReadOnlyList<TopLevel> Members { get; }
    public Token CloseBrace { get; }

    public NamespaceBlockBody(Token openBrace, IReadOnlyList<TopLevel> members, Token closeBrace)
    {
        OpenBrace = openBrace;
        Members = members;
        CloseBrace = closeBrace;
    }

}
