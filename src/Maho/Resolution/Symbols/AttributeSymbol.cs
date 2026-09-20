using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class AttributeSymbol : Symbol
{
    public AttributeFlags Flags { get; internal set; }
    public NamespaceTrieNode? ContainingNamespace { get; }

    public List<SymbolHandle> Attributes { get; internal set; }

    public List<SymbolHandle> Parameters { get; internal set; }
    public AttributeSignature? Syntax { get; }

    public AttributeSymbol(SymbolID id, SymbolPart name, Scope enclosingScope, NamespaceTrieNode? containingNamespace, AttributeSignature? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Attribute;
        Attributes = [];
        ContainingNamespace = containingNamespace;
        Parameters = [];
        Syntax = syntax;
    }
}