using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class AliasSymbol : Symbol
{
    public SymbolHandle? ContainingSymbol { get; }
    public NamespaceTrieNode? ContainingNamespace { get; }
    public ulong Flags { get; internal set; }

    public IReadOnlyList<SymbolHandle> TypeParameters { get; internal set; }
    public SymbolHandle Target { get; internal set; }

    public SyntaxNode? Syntax { get; }

    public AliasSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, SymbolHandle? containingSymbol, SyntaxNode? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Alias;
        ContainingSymbol = containingSymbol;
        TypeParameters = [];
        Syntax = syntax;
    }

    public AliasSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, SyntaxNode? syntax) : base(id, name, enclosingScope)
    {

        Kind = SymbolKind.Alias;
        ContainingNamespace = containingNamespace;
        TypeParameters = [];
        Syntax = syntax;
    }
}

