using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Local block statement that groups a sequence of local items. </summary>
internal sealed class LocalBlockStatement : LocalStatement
{
    /// <summary> Attributes attached to the block. </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    /// <summary> Modifiers attached to the block. </summary>
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> Opening brace token. </summary>
    public Token OpenBrace { get; }
    /// <summary> Local items contained in the block. </summary>
    public IReadOnlyList<Local> Locals { get; internal set; }
    /// <summary> Closing brace token. </summary>
    public Token CloseBrace { get; }

    /// <summary> Creates one local block statement node. </summary>
    public LocalBlockStatement(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, Token openBrace, IReadOnlyList<Local> locals, Token closeBrace)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        OpenBrace = openBrace;
        Locals = locals;
        CloseBrace = closeBrace;
    }
}
