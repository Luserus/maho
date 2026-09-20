using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class AliasSymbol : Symbol
{
    public SymbolHandle? ContainingSymbol { get; }
    public NamespaceTrieNode? ContainingNamespace { get; }
    public ulong Flags { get; internal set; }

    public IReadOnlyList<SymbolHandle> GenericParameters { get; internal set; }
    public TypeRef Target { get; internal set; } = TypeRef.Unresolved;
    public bool HasCompatibleConstraints { get; internal set; } = true;

    public AliasDeclaration? Syntax { get; }

    public AliasSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, SymbolHandle? containingSymbol, AliasDeclaration? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Alias;
        ContainingSymbol = containingSymbol;
        GenericParameters = [];
        Syntax = syntax;
    }

    public AliasSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, AliasDeclaration? syntax) : base(id, name, enclosingScope)
    {

        Kind = SymbolKind.Alias;
        ContainingNamespace = containingNamespace;
        GenericParameters = [];
        Syntax = syntax;
    }
}