using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Top-level block statement that groups a sequence of local items. </summary>
internal sealed class TopLevelBlockStatement : TopLevelStatement
{
    /// <summary> Opening brace token. </summary>
    public Token OpenBrace { get; }
    /// <summary> Local items contained in the block. </summary>
    public IReadOnlyList<Local> Locals { get; }
    /// <summary> Closing brace token. </summary>
    public Token CloseBrace { get; }

    /// <summary> Creates one top-level block statement node. </summary>
    public TopLevelBlockStatement(Token openBrace, IReadOnlyList<Local> locals, Token closeBrace)
    {
        OpenBrace = openBrace;
        Locals = locals;
        CloseBrace = closeBrace;
    }
}
