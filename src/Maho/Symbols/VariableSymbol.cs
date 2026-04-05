using Maho.Syntax;
using Maho.Resolution;

namespace Maho.Symbols;

/// <summary> Semantic symbol for a variable or field declaration. </summary>
internal sealed class VariableSymbol : DeclaredSymbol
{
    /// <summary> Resolved semantic type assigned to the variable once type binding has run. </summary>
    public ResolvedTypeReference? Type { get; private set; }

    /// <summary> Creates one declared variable symbol. </summary>
    public VariableSymbol(SymbolName name, Symbol parentSymbol, SyntaxNode declaration)
        : base(SymbolKind.Variable, name, parentSymbol, declaration) {}

    /// <summary> Records the resolved variable type. </summary>
    public void ResolveType(ResolvedTypeReference type) => Type = type;
}
