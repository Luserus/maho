using Maho.Syntax;

namespace Maho.Symbols;

/// <summary> Semantic symbol for one declared property. </summary>
internal sealed class PropertySymbol : DeclaredSymbol
{
    /// <summary> Creates one declared property symbol. </summary>
    public PropertySymbol(SymbolName name, Symbol parentSymbol, SyntaxNode declaration)
        : base(SymbolKind.Property, name, parentSymbol, declaration) { }
}
