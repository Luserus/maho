using System.Collections.Generic;
using Maho.Syntax;

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

    public IReadOnlyList<Symbol> GetSymbols(SymbolPart name)
    {
        if (SymbolsByName.TryGetValue(name, out var list))
            return list;

        if (name.Arity > 0)
        {
            foreach (var (key, symbols) in SymbolsByName)
            {
                if (key.Text == name.Text && key.Arity != name.Arity)
                {
                    foreach (var sym in symbols)
                    {
                        if (sym is TypeSymbol ts && ts.Syntax?.Name is GenericName gn && gn.GenericParameters is { Count: > 0 } gp &&
                            gp[^1].IsVariadic && name.Arity >= gp.Count - 1)
                        {
                            return symbols;
                        }
                    }
                }
            }
        }

        return [];
    }

    public Symbol? GetSymbol(SymbolHandle handle)
    {
        for (int i = 0; i < Symbols.Count; i++)
            if (ResolutionContext.GetHandle(Symbols[i]) == handle)
                return Symbols[i];

        return null;
    }
}