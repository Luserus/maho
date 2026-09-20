using System.Collections.Generic;

namespace Maho.Resolution;

internal sealed class NamespaceTrieNode
{
    public SymbolPart Name { get; set; }
    public NamespaceTrieNode? Parent { get; set; }
    public Dictionary<SymbolPart, NamespaceTrieNode> Next { get; } = [];
    public List<Symbol> Symbols { get; } = [];
    public Dictionary<SymbolPart, List<Symbol>> SymbolsByName { get; } = [];

    public void RegisterSymbol(Symbol symbol)
    {
        Symbols.Add(symbol);
        if (!SymbolsByName.TryGetValue(symbol.Name, out var list))
        {
            list = [];
            SymbolsByName[symbol.Name] = list;
        }
        list.Add(symbol);
    }

    public IReadOnlyList<Symbol> GetSymbols(SymbolPart name) =>
        SymbolsByName.TryGetValue(name, out var list) ? list : [];

    public Symbol? GetSymbol(SymbolHandle handle)
    {
        for (int i = 0; i < Symbols.Count; i++)
        {
            if (ResolutionContext.GetHandle(Symbols[i]) == handle)
                return Symbols[i];
        }
        return null;
    }
}