using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Semantic symbol representing a declared macro. </summary>
internal sealed class MacroSymbol : Symbol
{
    public SymbolHandle? ContainingSymbol { get; }
    public NamespaceTrieNode? ContainingNamespace { get; }
    public MacroDeclaration Syntax { get; }

    public MacroSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, SymbolHandle? containingSymbol, MacroDeclaration syntax)
        : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Macro;
        ContainingSymbol = containingSymbol;
        Syntax = syntax;
    }

    public MacroSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, NamespaceTrieNode? containingNamespace, MacroDeclaration syntax)
        : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Macro;
        ContainingNamespace = containingNamespace;
        Syntax = syntax;
    }
}
