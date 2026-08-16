using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Top-level declaration block whose variables bypass an implicit top-level entry function. </summary>
internal sealed class TopLevelGlobalBlock : TopLevel
{
    /// <summary> The <c>global</c> contextual keyword. </summary>
    public Token GlobalKeyword { get; }
    /// <summary> Opening brace token. </summary>
    public Token OpenBrace { get; }
    /// <summary> Top-level declarations contained in the global block. </summary>
    public IReadOnlyList<TopLevel> Members { get; }
    /// <summary> Closing brace token. </summary>
    public Token CloseBrace { get; }

    /// <summary> Creates one top-level global block. </summary>
    public TopLevelGlobalBlock(Token globalKeyword, Token openBrace, IReadOnlyList<TopLevel> members, Token closeBrace)
    {
        GlobalKeyword = globalKeyword;
        OpenBrace = openBrace;
        Members = members;
        CloseBrace = closeBrace;
    }
}
