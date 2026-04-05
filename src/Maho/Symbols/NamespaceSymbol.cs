using Maho.Syntax;

namespace Maho.Symbols;

/// <summary> Semantic symbol for a namespace container in the project symbol graph. </summary>
internal sealed class NamespaceSymbol : DeclaredSymbol
{
    /// <summary> Creates one namespace symbol under the provided parent container. </summary>
    public NamespaceSymbol(SymbolName name, Symbol? parentSymbol, SyntaxNode declaration)
        : base(SymbolKind.Namespace, name, parentSymbol, declaration) { }
}
