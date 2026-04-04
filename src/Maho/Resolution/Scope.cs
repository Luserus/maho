using System;
using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Represents one lexical scope and the symbols declared directly inside it. </summary>
internal sealed class Scope
{
    private readonly Dictionary<SymbolName, List<Symbol>> symbolsByName = [];
    private readonly List<Scope> children = [];
    private readonly List<Symbol> declaredSymbols = [];

    public Scope? Parent { get; }
    public Symbol? OwnerSymbol { get; }
    public SyntaxNode Boundary { get; }
    public IReadOnlyList<Scope> Children => children;
    public IReadOnlyList<Symbol> DeclaredSymbols => declaredSymbols;

    /// <summary> Creates a child scope and links it into the parent scope tree immediately. </summary>
    public Scope(Scope? parent, SyntaxNode boundary, Symbol? ownerSymbol = null)
    {
        Parent = parent;
        Boundary = boundary;
        OwnerSymbol = ownerSymbol;
        parent?.children.Add(this);
    }

    /// <summary>
    /// Registers a symbol in this scope without deciding whether the declaration is semantically
    /// legal. Duplicate and overload analysis happens later.
    /// </summary>
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

    public bool TryLookupLocal(SymbolName name, out IReadOnlyList<Symbol> symbols)
    {
        if (symbolsByName.TryGetValue(name, out List<Symbol>? declared))
        {
            symbols = declared;
            return true;
        }

        symbols = [];
        return false;
    }

    public IReadOnlyList<Symbol> LookupLocal(SymbolName name)
    {
        return symbolsByName.TryGetValue(name, out List<Symbol>? declared)
            ? declared
            : Array.Empty<Symbol>();
    }

    /// <summary> Returns every visible symbol with the requested name by walking outward through parent scopes. </summary>
    public IEnumerable<Symbol> Lookup(SymbolName name)
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
