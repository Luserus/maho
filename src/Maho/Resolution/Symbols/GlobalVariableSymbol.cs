using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class GlobalVariableSymbol : Symbol
{
    public VariableFlags Flags { get; internal set; }
    public NamespaceTrieNode? ContainingNamespace;
    
    public IReadOnlyList<SymbolHandle> TypeParameters { get; internal set; }
    public List<SymbolHandle> Attributes { get; internal set; }

    public SymbolHandle Type { get; internal set; }

    public VariableDeclaration? Syntax { get; }

    public GlobalVariableSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace,
                                VariableDeclaration? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.GlobalVariable;
        ContainingNamespace = containingNamespace;
        TypeParameters = [];
        Attributes = [];
        Syntax = syntax;
    }
}

