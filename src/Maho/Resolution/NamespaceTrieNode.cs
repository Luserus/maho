using System.Collections.Generic;

namespace Maho.Resolution;

internal sealed class NamespaceTrieNode
{
    public Dictionary<SymbolPart, NamespaceTrieNode> Next { get; } = [];
}

