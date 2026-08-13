using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Member block that groups a sequence of type-scope members. </summary>
internal sealed class MemberBlockDeclaration : Member
{
    /// <summary> Attributes attached to the block. </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    /// <summary> Modifiers attached to the block. </summary>
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> Opening brace token. </summary>
    public Token OpenBrace { get; }
    /// <summary> Members contained in the block. </summary>
    public IReadOnlyList<Member> Members { get; }
    /// <summary> Closing brace token. </summary>
    public Token CloseBrace { get; }

    /// <summary> Creates one type-scope member block node. </summary>
    public MemberBlockDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, Token openBrace, IReadOnlyList<Member> members, Token closeBrace)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        OpenBrace = openBrace;
        Members = members;
        CloseBrace = closeBrace;
    }
}
