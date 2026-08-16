using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class FunctionSymbol : Symbol
{
    public SymbolName Name { get; }
    public FunctionFlags Flags { get; internal set; }

    public NamespaceTrieNode? ContainingNamespace { get; }

    public IReadOnlyList<SymbolHandle> TypeParameters { get; }
    public List<SymbolHandle> Attributes { get; internal set; }

    public List<SymbolHandle> LocalVariables { get; internal set; }
    public List<SymbolHandle> LocalFunctions { get; internal set; }
    public List<SymbolHandle> LocalTypes { get; internal set; }

    public FunctionDeclaration? Syntax { get; }

    public FunctionSymbol(SymbolID id, Scope enclosingScope, SymbolName name, NamespaceTrieNode? containingNamespace, IReadOnlyList<SymbolHandle> typeParameters,
                        FunctionDeclaration? syntax) : base(id, enclosingScope)
    {
        Kind = SymbolKind.Function;
        Name = name;
        ContainingNamespace = containingNamespace;
        TypeParameters = typeParameters;
        Attributes = [];
        LocalVariables = [];
        LocalFunctions = [];
        LocalTypes = [];
        Syntax = syntax;
    }
}