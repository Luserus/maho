using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class CompilationUnit : SyntaxNode
{
    public IReadOnlyList<TopLevel> Members { get; }
    public Token EndToken { get; }

    public CompilationUnit(IReadOnlyList<TopLevel> members, Token endToken)
    {
        Members = members;
        EndToken = endToken;
    } 
}