using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class FunctionSymbol : Symbol
{
    public FunctionFlags Flags { get; internal set; }

    public NamespaceTrieNode? ContainingNamespace { get; }

    public IReadOnlyList<SymbolHandle> GenericParameters { get; internal set; }
    public List<SymbolHandle> Attributes { get; internal set; }

    public List<SymbolHandle> Parameters { get; internal set; }
    public List<LocalVariableSymbol> LocalVariables { get; internal set; }
    public List<SymbolHandle> LocalFunctions { get; internal set; }
    public List<SymbolHandle> LocalTypes { get; internal set; }

    public TypeRef ReturnType { get; internal set; } = TypeRef.Unresolved;

    public FunctionDeclaration? Syntax { get; }

    public LocalVariableSymbol this[int index] => LocalVariables[index];
    public LocalVariableSymbol this[SymbolID id] => LocalVariables[(int)id];
    public LocalVariableSymbol this[SymbolHandle handle] => LocalVariables[(int)handle.ID];

    public bool TryGetLocalVariable(int index, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LocalVariableSymbol? symbol)
    {
        if (index < LocalVariables.Count)
        {
            symbol = LocalVariables[index];
            return true;
        }

        symbol = null;
        return false;
    }

    public bool TryGetLocalVariable(SymbolID id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LocalVariableSymbol? symbol) =>
        TryGetLocalVariable((int)id, out symbol);

    public FunctionSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace,
                        FunctionDeclaration? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Function;
        ContainingNamespace = containingNamespace;
        GenericParameters = [];
        Attributes = [];
        Parameters = [];
        LocalVariables = [];
        LocalFunctions = [];
        LocalTypes = [];
        Syntax = syntax;
    }
}
