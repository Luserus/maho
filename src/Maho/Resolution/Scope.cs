using System;
using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class Scope
{
    private readonly Dictionary<string, List<Symbol>> symbolsByName = new(StringComparer.Ordinal);
    private readonly List<Scope> children = [];
    private readonly List<Symbol> declaredSymbols = [];

    public Scope? Parent { get; }
    public Symbol? OwnerSymbol { get; }
    public SyntaxNode Boundary { get; }
    public IReadOnlyList<Scope> Children => children;
    public IReadOnlyList<Symbol> DeclaredSymbols => declaredSymbols;


    public Scope(Scope? parent, SyntaxNode boundary, Symbol? ownerSymbol = null)
    {
        Parent = parent;
        Boundary = boundary;
        OwnerSymbol = ownerSymbol;
        parent?.children.Add(this);
    }

    public void Declare(Symbol symbol)
    {
        declaredSymbols.Add(symbol);

        if (!symbolsByName.TryGetValue(symbol.Name, out List<Symbol>? symbols))
        {
            symbols = [];
            symbolsByName.Add(symbol.Name, symbols);
        }

        symbols.Add(symbol);
    }

    public bool TryLookupLocal(string name, out IReadOnlyList<Symbol> symbols)
    {
        if (symbolsByName.TryGetValue(name, out List<Symbol>? declared))
        {
            symbols = declared;
            return true;
        }

        symbols = [];
        return false;
    }

    public IReadOnlyList<Symbol> LookupLocal(string name)
    {
        return symbolsByName.TryGetValue(name, out List<Symbol>? declared)
            ? declared
            : Array.Empty<Symbol>();
    }

    public IEnumerable<Symbol> Lookup(string name)
    {
        for (Scope? current = this; current is not null; current = current.Parent)
        {
            if (!current.symbolsByName.TryGetValue(name, out List<Symbol>? declared))
                continue;

            for (int i = 0; i < declared.Count; i++)
                yield return declared[i];
        }
    }
}
