using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Represents a block expression containing multiple statements. </summary>
internal sealed class BlockExpression : Expression
{
    /// <summary> Attributes attached to the block. </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    /// <summary> Modifiers attached to the block. </summary>
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> Opening brace token. </summary>
    public Token OpenBrace { get; }
    /// <summary> Local items contained in the block. </summary>
    public IReadOnlyList<Local> Locals { get; internal set; }
    /// <summary> Trailing / final expression of the block, if any. </summary>
    public Expression? FinalExpression { get; internal set; }
    /// <summary> Closing brace token. </summary>
    public Token CloseBrace { get; }

    public BlockExpression(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, Token openBrace, IReadOnlyList<Local> locals, Expression? finalExpression, Token closeBrace)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        OpenBrace = openBrace;
        Locals = locals;
        FinalExpression = finalExpression;
        CloseBrace = closeBrace;
    }

    public BlockExpression(Token openBrace, IReadOnlyList<Local> locals, Expression? finalExpression, Token closeBrace)
        : this([], [], openBrace, locals, finalExpression, closeBrace)
    {
    }
}