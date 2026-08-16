using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Top-level block that groups a sequence of top-level items. </summary>
internal sealed class TopLevelBlock : TopLevel
{
    /// <summary> Attributes attached to the block. </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    /// <summary> Modifiers attached to the block. </summary>
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> Opening brace token. </summary>
    public Token OpenBrace { get; }
    /// <summary> Top-level items contained in the block. </summary>
    public IReadOnlyList<TopLevel> Members { get; }
    /// <summary> Closing brace token. </summary>
    public Token CloseBrace { get; }

    /// <summary> Creates one top-level block node. </summary>
    public TopLevelBlock(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, Token openBrace, IReadOnlyList<TopLevel> members, Token closeBrace)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        OpenBrace = openBrace;
        Members = members;
        CloseBrace = closeBrace;
    }
}
