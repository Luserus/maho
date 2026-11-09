using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Type body for enum declarations that store variants and methods. </summary>
internal sealed class TypeEnumBody : TypeBody
{
    public Token OpenBrace { get; }
    public IReadOnlyList<EnumVariant> Variants { get; }
    public IReadOnlyList<Member> Members { get; }
    public Token CloseBrace { get; }

    public TypeEnumBody(Token openBrace, IReadOnlyList<EnumVariant> variants, IReadOnlyList<Member> members, Token closeBrace)
    {
        OpenBrace = openBrace;
        Variants = variants;
        Members = members;
        CloseBrace = closeBrace;
    }
}
