using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class GlobalVariableSymbol : Symbol
{
    public SymbolName Name { get; }
    public VariableFlags Flags { get; internal set; }
    public NamespaceTrieNode? ContainingNamespace;
    
    public IReadOnlyList<SymbolHandle> TypeParameters { get; }
    public List<SymbolHandle> Attributes { get; internal set; }

    public SymbolHandle Type { get; internal set; }

    public VariableDeclaration? Syntax { get; }

    public GlobalVariableSymbol(SymbolID id, Scope enclosingScope, SymbolName name, NamespaceTrieNode? containingNamespace, IReadOnlyList<SymbolHandle> typeParameters,
                                VariableDeclaration? syntax) : base(id, enclosingScope)
    {
        Kind = SymbolKind.GlobalVariable;
        Name = name;
        ContainingNamespace = containingNamespace;
        TypeParameters = typeParameters;
        Attributes = [];
        Syntax = syntax;
    }
}

