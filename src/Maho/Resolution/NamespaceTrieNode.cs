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
                        if (sym is TypeSymbol ts && ts.Syntax?.Name is GenericName gn && gn.GenericParameters is { Count: > 0 } gp)
                        {
                            if (gp[^1].IsVariadic && name.Arity >= gp.Count - 1)
                                return symbols;

                            if (key.Arity > name.Arity)
                            {
                                bool allDefaults = true;
                                for (int p = name.Arity; p < gp.Count; p++)
                                {
                                    if (gp[p].DefaultType == null)
                                    {
                                        allDefaults = false;
                                        break;
                                    }
                                }
                                if (allDefaults)
                                    return symbols;
                            }
                        }
                    }
                }
            }
        }
        else
        {
            foreach (var (key, symbols) in SymbolsByName)
            {
                if (key.Text != name.Text)
                    continue;

                foreach (var sym in symbols)
                {
                    if (sym is TypeSymbol ts && ts.Syntax?.Name is GenericName gn && gn.GenericParameters is { Count: > 0 } gp)
                    {
                        bool allThis = true;
                        for (int p = 0; p < gp.Count; p++)
                        {
                            if (gp[p].Kind is not GenericParameterKind.This)
                            {
                                allThis = false;
                                break;
                            }
                        }
                        if (allThis)
                            return symbols;
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