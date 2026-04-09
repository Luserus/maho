using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Root syntax node for one parsed source file. </summary>
internal sealed class CompilationUnit : SyntaxNode
{
    /// <summary> Top-level members contained in the file. </summary>
    public IReadOnlyList<TopLevel> Members { get; }
    /// <summary> Synthetic end-of-file token closing the unit. </summary>
    public Token EndToken { get; }

    /// <summary> Creates one compilation unit from its members and end token. </summary>
    public CompilationUnit(IReadOnlyList<TopLevel> members, Token endToken)
    {
        Members = members;
        EndToken = endToken;
    } 
}
