using Maho.Syntax;

namespace Maho.Symbols;

internal sealed class NamespaceSymbol : DeclaredSymbol
{
    public NamespaceSymbol(SymbolName name, Symbol? parentSymbol, SyntaxNode declaration)
        : base(SymbolKind.Namespace, name, parentSymbol, declaration) { }
}
