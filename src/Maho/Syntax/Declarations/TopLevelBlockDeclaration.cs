using System;
using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Top-level block that groups a sequence of top-level items, optionally attached to a keyword like 'global' or 'const'. </summary>
internal sealed class TopLevelBlockDeclaration : TopLevelDeclaration
{
    /// <summary> The attached keyword token ('global', 'const'), if any. </summary>
    public Token? AttachedKeyword { get; }
    /// <summary> Opening brace token. </summary>
    public Token OpenBrace { get; }
    /// <summary> Top-level items contained in the block. </summary>
    public IReadOnlyList<TopLevel> Members { get; internal set; }
    /// <summary> Closing brace token. </summary>
    public Token CloseBrace { get; }

    /// <summary> Attributes attached to the block (always empty; block scopes do not accept attributes). </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes => Array.Empty<AttributeListSyntax>();

    /// <summary> Modifiers attached to the block (contains AttachedKeyword if present). </summary>
    public IReadOnlyList<Token> Modifiers => AttachedKeyword is not null ? new[] { AttachedKeyword } : Array.Empty<Token>();

    /// <summary> Creates one top-level block node with an optional attached keyword. </summary>
    public TopLevelBlockDeclaration(Token? attachedKeyword, Token openBrace, IReadOnlyList<TopLevel> members, Token closeBrace)
    {
        AttachedKeyword = attachedKeyword;
        OpenBrace = openBrace;
        Members = members;
        CloseBrace = closeBrace;
    }

    /// <summary> Compatibility constructor. </summary>
    public TopLevelBlockDeclaration(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, Token openBrace, IReadOnlyList<TopLevel> members, Token closeBrace)
        : this(modifiers.Count > 0 ? modifiers[0] : null, openBrace, members, closeBrace)
    {
    }
}
