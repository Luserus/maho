using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class TypeBlockBody : TypeBody
{
    public Token OpenBrace { get; }
    public IReadOnlyList<Member> Members { get; }
    public Token CloseBrace { get; }

    public TypeBlockBody(Token openBrace, IReadOnlyList<Member> members, Token closeBrace)
    {
        OpenBrace = openBrace;
        Members = members;
        CloseBrace = closeBrace;
    }
}