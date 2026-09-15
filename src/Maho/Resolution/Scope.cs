using System.Collections.Generic;
using System.Linq;

namespace Maho.Resolution;

internal sealed class Scope
{
    public Scope? Parent { get; }
    public Dictionary<SymbolHandle, Symbol> Symbols { get; }
    public Dictionary<SymbolPart, List<Symbol>> SymbolsByName { get; }

    public Dictionary<SymbolHandle, Scope> ChildScopes { get; }

    public static Scope GlobalScope { get; } = new Scope(null);

    public Scope(Scope? parent)
    {
        Parent = parent;
        Symbols = [];
        SymbolsByName = [];
        ChildScopes = [];
    }

    public Scope? GetChildScope(SymbolHandle handle) => ChildScopes.GetValueOrDefault(handle);

    public IReadOnlyList<Symbol> GetLocalSymbols(SymbolPart name) => SymbolsByName.TryGetValue(name, out var symbols) ? symbols : [];

    public IReadOnlyList<Symbol> GetSymbols(SymbolPart name)
    {
        var locals = GetLocalSymbols(name);

        if (locals.Count != 0)
            return locals;

        return Parent?.GetSymbols(name) ?? [];
    }

    public Symbol? GetSymbol(SymbolHandle handle) => Symbols.TryGetValue(handle, out var symbol) ? symbol : Parent?.GetSymbol(handle);


    public static IReadOnlyList<Symbol> Resolve(Scope scope, SymbolName name)
    {
        if (name.Count == 0)
            return [];

        // First component: lexical lookup.
        var start = scope[name[0]];

        if (start.Count == 0)
            return [];

        for (int i = 0; i < name.Count - 1; i++)
        {
            var symbol = start[0];

            var child = scope.GetChildScope((symbol.Kind, symbol.ID));

            if (child is null)
                return [];

            scope = child;

            // Remaining components: local lookup.
            start = scope.GetLocalSymbols(name[i + 1]);

            if (start.Count == 0)
                return [];
        }

        return start;
    }

    public Symbol? this[SymbolHandle handle] => GetSymbol(handle);

    public IReadOnlyList<Symbol> this[SymbolPart name] => GetSymbols(name);

    public IReadOnlyList<Symbol> this[SymbolName name] => Resolve(this, name);
}
