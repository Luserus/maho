using Maho.Syntax;

namespace Maho.Symbols;

internal abstract class DeclaredSymbol : Symbol
{
    public SyntaxNode Declaration { get; }

    protected DeclaredSymbol(SymbolKind kind, string name, Symbol? parentSymbol, SyntaxNode declaration)
        : base(kind, name, parentSymbol) => Declaration = declaration;
}