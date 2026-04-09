using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Type body represented as a braced block. </summary>
internal sealed class TypeBlockBody : TypeBody
{
    /// <summary> Opening brace token. </summary>
    public Token OpenBrace { get; }
    /// <summary> Members declared inside the type body. </summary>
    public IReadOnlyList<Member> Members { get; }
    /// <summary> Closing brace token. </summary>
    public Token CloseBrace { get; }

    /// <summary> Creates one type-block body node. </summary>
    public TypeBlockBody(Token openBrace, IReadOnlyList<Member> members, Token closeBrace)
    {
        OpenBrace = openBrace;
        Members = members;
        CloseBrace = closeBrace;
    }
}
