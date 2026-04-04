using Maho.Syntax;
using Maho.Resolution;

namespace Maho.Symbols;

internal sealed class VariableSymbol : DeclaredSymbol
{
    public ResolvedTypeReference? Type { get; private set; }

    public VariableSymbol(SymbolName name, Symbol parentSymbol, SyntaxNode declaration)
        : base(SymbolKind.Variable, name, parentSymbol, declaration) {}

    public void ResolveType(ResolvedTypeReference type) => Type = type;
}
