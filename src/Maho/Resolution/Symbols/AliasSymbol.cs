using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class AliasSymbol : Symbol
{
    public SymbolName Name { get; }
    public SymbolHandle? ContainingSymbol { get; }
    public NamespaceTrieNode? ContainingNamespace { get; }
    public ulong Flags { get; internal set; }

    public SymbolHandle Target { get; internal set; }

    public SyntaxNode? Syntax { get; }

    public AliasSymbol(SymbolID id, Scope enclosingScope, SymbolName name, SymbolHandle? containingSymbol, SyntaxNode? syntax) : base(id, enclosingScope)
    {
        Name = name;
        Kind = SymbolKind.Alias;
        ContainingSymbol = containingSymbol;
        Syntax = syntax;
    }

    public AliasSymbol(SymbolID id, Scope enclosingScope, SymbolName name, NamespaceTrieNode? containingNamespace, SyntaxNode? syntax) : base(id, enclosingScope)
    {
        Name = name;
        Kind = SymbolKind.Alias;
        ContainingNamespace = containingNamespace;
        Syntax = syntax;
    }
}

