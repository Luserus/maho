using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class FunctionSymbol : Symbol
{
    public FunctionFlags Flags { get; internal set; }

    public NamespaceTrieNode? ContainingNamespace { get; }

    public IReadOnlyList<SymbolHandle> TypeParameters { get; internal set; }
    public List<SymbolHandle> Attributes { get; internal set; }

    public List<SymbolHandle> LocalVariables { get; internal set; }
    public List<SymbolHandle> LocalFunctions { get; internal set; }
    public List<SymbolHandle> LocalTypes { get; internal set; }

    public SymbolHandle ReturnType { get; internal set; }

    public FunctionDeclaration? Syntax { get; }

    public FunctionSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace,
                        FunctionDeclaration? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Function;
        ContainingNamespace = containingNamespace;
        TypeParameters = [];
        Attributes = [];
        LocalVariables = [];
        LocalFunctions = [];
        LocalTypes = [];
        Syntax = syntax;
    }
}