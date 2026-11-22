using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>
/// Represents a compile-time constant symbol declared at top-level, within a type, or locally.
/// Stored in a dedicated symbol table to avoid runtime data segment allocations and optimize incremental analysis.
/// </summary>
internal sealed class ConstantSymbol : Symbol
{
    public VariableFlags Flags { get; internal set; }
    public NamespaceTrieNode? ContainingNamespace { get; internal set; }
    public SymbolHandle Parent { get; internal set; }

    public List<SymbolHandle> Attributes { get; internal set; } = [];

    public TypeRef Type { get; internal set; } = TypeRef.Unresolved;
    public ConstantValue Value { get; internal set; } = ConstantValue.None;

    public ConstantDeclaration? Syntax { get; }

    public ConstantSymbol(
        SymbolID id,
        Scope enclosingScope,
        SymbolPart name,
        NamespaceTrieNode? containingNamespace,
        ConstantDeclaration? syntax) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Constant;
        ContainingNamespace = containingNamespace;
        Syntax = syntax;
    }
}
