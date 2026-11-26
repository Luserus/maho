using System.Collections.Generic;
using System.Linq;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class Scope
{
    public Scope? Parent { get; }
    public Dictionary<SymbolHandle, Symbol> Symbols { get; }
    public Dictionary<SymbolPart, List<Symbol>> SymbolsByName { get; }
    public Dictionary<SymbolHandle, Scope> ChildScopes { get; }

    /// <summary> Scopes imported from referenced projects or external modules. </summary>
    public List<Scope> ImportedScopes { get; } = [];

    /// <summary> Namespaces imported into this scope via using directives. </summary>
    public List<NamespaceTrieNode> UsingNamespaces { get; } = [];

    /// <summary> External module aliases brought into this lexical scope via <c>extern using &lt;Alias&gt;;</c>. </summary>
    public Dictionary<string, ModuleSymbol> ExternModuleAliases { get; } = new(System.StringComparer.Ordinal);

    public bool TryLookupExternAlias(string alias, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ModuleSymbol? module)
    {
        for (var current = this; current != null; current = current.Parent)
        {
            if (current.ExternModuleAliases.TryGetValue(alias, out module))
                return true;
        }
        module = null;
        return false;
    }

    private NamespaceTrieNode? globalNamespace;

    /// <summary> Root namespace trie node for namespace navigation. </summary>
    public NamespaceTrieNode? GlobalNamespace
    {
        get => globalNamespace ?? Parent?.GlobalNamespace;
        set => globalNamespace = value;
    }

    public static Scope GlobalScope { get; } = new Scope(null);

    /// <summary> Indicates whether this scope is a sink scope for throwaway symbols. </summary>
    public bool IsSink { get; set; }

    public Scope(Scope? parent)
    {
        Parent = parent;
        IsSink = parent?.IsSink ?? false;
        Symbols = [];
        SymbolsByName = [];
        ChildScopes = [];
    }

    public Scope? GetChildScope(SymbolHandle handle)
    {
        for (var current = this; current != null; current = current.Parent)
        {
            if (current.ChildScopes.TryGetValue(handle, out var scope))
                return scope;

            for (int i = 0; i < current.ImportedScopes.Count; i++)
            {
                var imported = current.ImportedScopes[i].GetChildScope(handle);

                if (imported is not null)
                    return imported;
            }
        }

        return null;
    }

    public IReadOnlyList<Symbol> GetLocalSymbols(SymbolPart name)
    {
        if (SymbolsByName.TryGetValue(name, out var symbols))
            return symbols;

        foreach (var (key, list) in SymbolsByName)
        {
            if (key.Text != name.Text)
                continue;

            foreach (var sym in list)
            {
                SeparatedSyntaxList<GenericParameterSyntax>? gp = sym switch
                {
                    TypeSymbol ts when ts.Syntax?.Name is GenericName gn => gn.GenericParameters,
                    NestedTypeSymbol nts when nts.Syntax?.Name is GenericName ngn => ngn.GenericParameters,
                    _ => null
                };
                if (gp is not { Count: > 0 } gpList)
                    continue;

                if (name.Arity > 0)
                {
                    if (key.Arity == name.Arity)
                        continue;

                    if (gpList[^1].IsVariadic && name.Arity >= gpList.Count - 1)
                        return list;

                    if (key.Arity > name.Arity)
                    {
                        bool allDefaults = true;
                        for (int p = name.Arity; p < gpList.Count; p++)
                        {
                            if (gpList[p].DefaultType is null)
                            {
                                allDefaults = false;
                                break;
                            }
                        }
                        if (allDefaults)
                            return list;
                    }
                }
                else
                {
                    bool allThis = true;
                    for (int p = 0; p < gpList.Count; p++)
                    {
                        if (gpList[p].Kind is not GenericParameterKind.This)
                        {
                            allThis = false;
                            break;
                        }
                    }
                    if (allThis)
                        return list;
                }
            }
        }

        return [];
    }

    private static bool IsTypeSymbol(Symbol s) =>
        s is TypeSymbol or NestedTypeSymbol or GenericParameterSymbol or AliasSymbol;

    public IReadOnlyList<Symbol> GetTypeSymbols(SymbolPart name)
    {
        // 1. Lexical declarations: check locals in this scope and all enclosing scopes
        for (var current = this; current != null; current = current.Parent)
        {
            var locals = current.GetLocalSymbols(name);
            var typeLocals = locals.Where(IsTypeSymbol).ToList();

            if (typeLocals.Count != 0)
                return typeLocals;
        }

        // 2. Using namespaces: check imported namespaces from closest enclosing scope outward
        for (var current = this; current != null; current = current.Parent)
        {
            if (current.UsingNamespaces.Count != 0)
            {
                List<Symbol>? found = null;

                for (int i = 0; i < current.UsingNamespaces.Count; i++)
                {
                    var matches = current.UsingNamespaces[i].GetSymbols(name);

                    for (int j = 0; j < matches.Count; j++)
                    {
                        if (IsTypeSymbol(matches[j]))
                        {
                            found ??= [];
                            found.Add(matches[j]);
                        }
                    }
                }

                if (found != null)
                    return found;
            }
        }

        // 3. External module / project imports
        for (var current = this; current != null; current = current.Parent)
        {
            if (current.ImportedScopes.Count != 0)
            {
                List<Symbol>? imported = null;

                for (int i = 0; i < current.ImportedScopes.Count; i++)
                {
                    var matches = current.ImportedScopes[i].GetSymbols(name);

                    for (int j = 0; j < matches.Count; j++)
                    {
                        if (IsTypeSymbol(matches[j]))
                        {
                            imported ??= [];
                            if (!imported.Contains(matches[j]))
                                imported.Add(matches[j]);
                        }
                    }
                }

                if (imported != null)
                    return imported;
            }
        }

        return [];
    }

    public static IReadOnlyList<Symbol> ResolveType(Scope scope, SymbolName name)
    {
        if (name.Count == 0)
            return [];

        // First component: lexical type lookup.
        var start = scope.GetTypeSymbols(name[0]);

        if (start.Count == 0)
        {
            var nsNode = ResolveNamespace(scope, name[0]);

            if (nsNode != null)
                return ResolveInNamespace(scope, nsNode, name, 1).Where(IsTypeSymbol).ToList();

            return [];
        }

        for (int i = 0; i < name.Count - 1; i++)
        {
            var symbol = start[0];
            var handle = ResolutionContext.GetHandle(symbol);
            var child = scope.GetChildScope(handle) ?? symbol.EnclosingScope?.GetChildScope(handle);

            if (child is null)
                return [];

            scope = child;

            // Remaining components: local lookup filtered to types.
            start = scope.GetLocalSymbols(name[i + 1]).Where(IsTypeSymbol).ToList();

            if (start.Count == 0)
                return [];
        }

        return start;
    }

    public IReadOnlyList<Symbol> ResolveType(SymbolName name) => ResolveType(this, name);

    public IReadOnlyList<Symbol> GetSymbols(SymbolPart name)
    {
        // 1. Lexical declarations: check locals in this scope and all enclosing scopes
        for (var current = this; current != null; current = current.Parent)
        {
            var locals = current.GetLocalSymbols(name);

            if (locals.Count != 0)
                return locals;
        }

        // 2. Using namespaces: check imported namespaces from closest enclosing scope outward
        for (var current = this; current != null; current = current.Parent)
        {
            if (current.UsingNamespaces.Count != 0)
            {
                List<Symbol>? found = null;

                for (int i = 0; i < current.UsingNamespaces.Count; i++)
                {
                    var matches = current.UsingNamespaces[i].GetSymbols(name);

                    if (matches.Count != 0)
                    {
                        found ??= [];

                        for (int j = 0; j < matches.Count; j++)
                            found.Add(matches[j]);
                    }
                }

                if (found != null)
                    return found;
            }
        }

        // 3. External module / project imports
        for (var current = this; current != null; current = current.Parent)
        {
            if (current.ImportedScopes.Count != 0)
            {
                List<Symbol>? imported = null;

                for (int i = 0; i < current.ImportedScopes.Count; i++)
                {
                    var matches = current.ImportedScopes[i].GetSymbols(name);

                    if (matches.Count != 0)
                    {
                        imported ??= [];

                        for (int j = 0; j < matches.Count; j++)
                        {
                            if (!imported.Contains(matches[j]))
                                imported.Add(matches[j]);
                        }
                    }
                }

                if (imported != null)
                    return imported;
            }
        }

        return [];
    }

    public Symbol? GetSymbol(SymbolHandle handle)
    {
        for (var current = this; current != null; current = current.Parent)
        {
            if (current.Symbols.TryGetValue(handle, out var symbol))
                return symbol;
        }

        for (var current = this; current != null; current = current.Parent)
        {
            for (int i = 0; i < current.UsingNamespaces.Count; i++)
            {
                var nsSymbol = current.UsingNamespaces[i].GetSymbol(handle);

                if (nsSymbol != null)
                    return nsSymbol;
            }
        }

        for (var current = this; current != null; current = current.Parent)
        {
            for (int i = 0; i < current.ImportedScopes.Count; i++)
            {
                var importedSymbol = current.ImportedScopes[i].GetSymbol(handle);

                if (importedSymbol != null)
                    return importedSymbol;
            }
        }

        return null;
    }

    public static IReadOnlyList<Symbol> Resolve(Scope scope, SymbolName name)
    {
        if (name.Count == 0)
            return [];

        // First component: lexical lookup.
        var start = scope[name[0]];

        if (start.Count == 0)
        {
            // Check if name begins with a namespace (e.g. Std.Int32 or Std.Collections.List)
            var nsNode = ResolveNamespace(scope, name[0]);

            if (nsNode != null)
                return ResolveInNamespace(scope, nsNode, name, 1);

            return [];
        }

        for (int i = 0; i < name.Count - 1; i++)
        {
            var symbol = start[0];
            var handle = ResolutionContext.GetHandle(symbol);
            var child = scope.GetChildScope(handle) ?? symbol.EnclosingScope?.GetChildScope(handle);

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

    private static NamespaceTrieNode? ResolveNamespace(Scope scope, SymbolPart name)
    {
        // 1. Check namespaces imported via using directives in this scope or parent scopes
        for (var current = scope; current != null; current = current.Parent)
            for (int i = 0; i < current.UsingNamespaces.Count; i++)
                if (current.UsingNamespaces[i].Next.TryGetValue(name, out var childNs))
                    return childNs;

        // 2. Check root namespace
        if (scope.GlobalNamespace != null && scope.GlobalNamespace.Next.TryGetValue(name, out var rootNs))
            return rootNs;

        return null;
    }

    private static IReadOnlyList<Symbol> ResolveInNamespace(Scope scope, NamespaceTrieNode nsNode, SymbolName name, int index)
    {
        while (index < name.Count)
        {
            var currentPart = name[index];

            // If not the last component and matches a child namespace, traverse deeper
            if (index < name.Count - 1 && nsNode.Next.TryGetValue(currentPart, out var nextNs))
            {
                nsNode = nextNs;
                index++;
                continue;
            }

            // Otherwise, look up symbol in the namespace
            var symbols = nsNode.GetSymbols(currentPart);
            if (symbols.Count == 0)
                return [];

            if (index == name.Count - 1)
                return symbols;

            // More components follow (e.g. Type.NestedType)
            var symbol = symbols[0];
            var handle = ResolutionContext.GetHandle(symbol);
            var childScope = scope.GetChildScope(handle) ?? symbol.EnclosingScope?.GetChildScope(handle);

            if (childScope is null)
                return [];

            scope = childScope;
            index++;
            var nextSymbols = scope.GetLocalSymbols(name[index]);

            if (nextSymbols.Count == 0)
                return [];

            for (int i = index; i < name.Count - 1; i++)
            {
                var nextSymbol = nextSymbols[0];
                var nextHandle = ResolutionContext.GetHandle(nextSymbol);
                var nextChild = scope.GetChildScope(nextHandle) ?? nextSymbol.EnclosingScope?.GetChildScope(nextHandle);

                if (nextChild is null)
                    return [];

                scope = nextChild;
                nextSymbols = scope.GetLocalSymbols(name[i + 1]);

                if (nextSymbols.Count == 0)
                    return [];
            }

            return nextSymbols;
        }

        return [];
    }

    public Symbol? this[SymbolHandle handle] => GetSymbol(handle);

    public IReadOnlyList<Symbol> this[SymbolPart name] => GetSymbols(name);

    public IReadOnlyList<Symbol> this[SymbolName name] => Resolve(this, name);
}