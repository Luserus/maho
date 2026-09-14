using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class ResolvedTree
{
    private readonly Dictionary<SyntaxNode, SymbolHandle> references = [];

    public void AddReference(SyntaxNode syntax, SymbolHandle symbol) => references[syntax] = symbol;

    public bool TryGetReference(SyntaxNode syntax, out SymbolHandle symbol) => references.TryGetValue(syntax, out symbol);

}
