using Maho.Syntax;

namespace Maho.Symbols;

internal abstract class DeclaredSymbol : Symbol
{
    /// <summary> Original syntax node that introduced this declared symbol. </summary>
    public SyntaxNode Declaration { get; }

    /// <summary> Creates one declaration-backed semantic symbol. </summary>
    protected DeclaredSymbol(SymbolKind kind, SymbolName name, Symbol? parentSymbol, SyntaxNode declaration)
        : base(kind, name, parentSymbol) => Declaration = declaration;
}
