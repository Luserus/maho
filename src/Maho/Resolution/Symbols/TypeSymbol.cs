using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal abstract class TypeSymbol : Symbol
{
    public TypeKind TypeKind { get; }
    public TypeFlags Flags { get; internal set; }
    public NamespaceTrieNode? ContainingNamespace { get; }

    public List<TypeRef> BaseTypes { get; internal set; }

    public IReadOnlyList<SymbolHandle> GenericParameters { get; internal set; }
    public List<SymbolHandle> Attributes { get; internal set; }

    public TypeDeclaration? Syntax { get; }

    public TypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind, NamespaceTrieNode? containingNamespace,
                    TypeDeclaration? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Type;
        TypeKind = typeKind;
        ContainingNamespace = containingNamespace;
        GenericParameters = [];
        BaseTypes = [];
        Attributes = [];
        
        Syntax = syntax;
    }
}
