using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class SumTypeSymbol : TypeSymbol
{
    public SumTypeSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, TypeKind typeKind, NamespaceTrieNode? containingNamespace, TypeDeclaration? syntax)
    : base(id, enclosingScope, name, typeKind, containingNamespace, syntax)
    {

    }
}