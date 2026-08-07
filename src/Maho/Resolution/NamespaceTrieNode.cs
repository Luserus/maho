using System.Collections.Generic;

namespace Maho.Resolution;

internal sealed class NamespaceTrieNode
{
    public Dictionary<SymbolName, NamespaceTrieNode> Next { get; } = [];
}

