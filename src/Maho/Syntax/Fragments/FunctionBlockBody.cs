using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Function body represented as a braced block. </summary>
internal sealed class FunctionBlockBody : FunctionBody
{
    /// <summary> Opening brace token. </summary>
    public Token OpenBrace { get; }
    /// <summary> Local items inside the function body. </summary>
    public IReadOnlyList<Local> Locals { get; }
    /// <summary> Closing brace token. </summary>
    public Token CloseBrace { get; }

    /// <summary> Creates one function-block body node. </summary>
    public FunctionBlockBody(Token openBrace, IReadOnlyList<Local> locals, Token closeBrace)
    {
        OpenBrace = openBrace;
        Locals = locals;
        CloseBrace = closeBrace;
    }
}
