using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class ResolutionContext
{
    public SyntaxTree syntaxTree { get; }
    public ResolvedTree resolvedTree { get; }

    public List<Symbol> Symbols;
    public List<Scope> Scopes;

    private int scopeID;
    private int symbolID;

    public ResolutionContext(SyntaxTree syntaxTree, ResolvedTree resolvedTree, List<Symbol> symbols, List<Scope> scopes, int scopeID, int symbolID)
    {
        this.syntaxTree = syntaxTree;
        this.resolvedTree = resolvedTree;

        Symbols = symbols;
        Scopes = scopes;

        this.scopeID = scopeID;
        this.symbolID = symbolID;
    }

    public Scope CreateScope(Scope? parent)
    {
        var scope = new Scope(scopeID++, parent);
        Scopes.Add(scope);
        return scope;
    }

    public NamespaceSymbol CreateNamespaceSymbol(Scope scope, Symbol? parent)
    {
        var symbol = new NamespaceSymbol(symbolID++, SymbolKind.Namespace, scope, parent);
        Symbols.Add(symbol);
        return symbol;
    }
}

