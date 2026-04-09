using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Local block statement that groups a sequence of local items. </summary>
internal sealed class LocalBlockStatement : LocalStatement
{
    /// <summary> Opening brace token. </summary>
    public Token OpenBrace { get; }
    /// <summary> Local items contained in the block. </summary>
    public IReadOnlyList<Local> Locals { get; }
    /// <summary> Closing brace token. </summary>
    public Token CloseBrace { get; }

    /// <summary> Creates one local block statement node. </summary>
    public LocalBlockStatement(Token openBrace, IReadOnlyList<Local> locals, Token closeBrace)
    {
        OpenBrace = openBrace;
        Locals = locals;
        CloseBrace = closeBrace;
    }
}
