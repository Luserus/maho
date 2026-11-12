using System;
using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Member block that groups a sequence of type-scope members, optionally attached to a keyword like 'const'. </summary>
internal sealed class MemberBlockDeclaration : Member
{
    /// <summary> The attached keyword token ('const', etc.), if any. </summary>
    public Token? AttachedKeyword { get; }
    /// <summary> Opening brace token. </summary>
    public Token OpenBrace { get; }
    /// <summary> Members contained in the block. </summary>
    public IReadOnlyList<Member> Members { get; }
    /// <summary> Closing brace token. </summary>
    public Token CloseBrace { get; }

    /// <summary> Attributes attached to the block (always empty; block scopes do not accept attributes). </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes => Array.Empty<AttributeListSyntax>();

    /// <summary> Modifiers attached to the block (contains AttachedKeyword if present). </summary>
    public IReadOnlyList<Token> Modifiers => AttachedKeyword is not null ? new[] { AttachedKeyword } : Array.Empty<Token>();

    /// <summary> Creates one type-scope member block node. </summary>
    public MemberBlockDeclaration(Token? attachedKeyword, Token openBrace, IReadOnlyList<Member> members, Token closeBrace)
    {
        AttachedKeyword = attachedKeyword;
        OpenBrace = openBrace;
        Members = members;
        CloseBrace = closeBrace;
    }

    /// <summary> Compatibility constructor. </summary>
    public MemberBlockDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, Token openBrace, IReadOnlyList<Member> members, Token closeBrace)
        : this(modifiers.Count > 0 ? modifiers[0] : null, openBrace, members, closeBrace)
    {
    }
}
