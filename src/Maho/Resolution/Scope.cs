using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Represents one lexical scope and the symbols declared directly inside it. </summary>
internal sealed class Scope
{
    /// <summary>
    /// Name table for declarations introduced directly in this scope. The list value preserves
    /// structurally coexisting same-name symbols until a later pass decides whether they are legal.
    /// </summary>
    private readonly Dictionary<SymbolName, List<Symbol>> symbolsByName = [];
    /// <summary> Nested scopes created directly under this scope. </summary>
    private readonly List<Scope> children = [];
    /// <summary> Flat declaration list preserved in declaration order for diagnostics/debugging. </summary>
    private readonly List<Symbol> declaredSymbols = [];

    /// <summary> Lexically enclosing scope, or <see langword="null"/> for the global scope. </summary>
    public Scope? Parent { get; }
    /// <summary> Symbol that owns this scope when the scope corresponds to a declaration container. </summary>
    public Symbol? OwnerSymbol { get; }
    /// <summary> Syntax node used as the semantic boundary for this scope. </summary>
    public SyntaxNode Boundary { get; }
    /// <summary> Directly nested child scopes. </summary>
    public IReadOnlyList<Scope> Children => children;
    /// <summary> Symbols declared directly in this scope, in discovery order. </summary>
    public IReadOnlyList<Symbol> DeclaredSymbols => declaredSymbols;

    /// <summary> Creates a child scope and links it into the parent scope tree immediately. </summary>
    public Scope(Scope? parent, SyntaxNode boundary, Symbol? ownerSymbol = null)
    {
        Parent = parent;
        Boundary = boundary;
        OwnerSymbol = ownerSymbol;
        // The scope tree mirrors lexical nesting, so child registration happens immediately at
        // construction time rather than through a separate bookkeeping pass.
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

    /// <summary> Attempts to resolve symbols with the requested name declared directly in this scope. </summary>
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

    /// <summary>
    /// Resolves symbols with the requested name declared directly in this scope, returning an empty
    /// list when the scope has no such declarations.
    /// </summary>
    public IReadOnlyList<Symbol> LookupLocal(SymbolName name)
    {
        return symbolsByName.TryGetValue(name, out List<Symbol>? declared)
            ? declared
            : [];
    }

    /// <summary>
    /// Returns every visible symbol with the requested name by walking outward through parent
    /// scopes. This is purely lexical lookup; later passes still decide which candidate wins.
    /// </summary>
    public IEnumerable<Symbol> Lookup(SymbolName name)
    {
        for (Scope? current = this; current is not null; current = current.Parent)
        {
            // Each scope contributes only its own declarations. Walking parent links produces the
            // normal lexical visibility chain.
            if (!current.symbolsByName.TryGetValue(name, out List<Symbol>? declared))
                continue;

            for (int i = 0; i < declared.Count; i++)
                yield return declared[i];
        }
    }
}
