using Maho.Syntax;
using Maho.Resolution;

namespace Maho.Symbols;

internal sealed class ParameterSymbol : DeclaredSymbol
{
    public int Ordinal { get; }
    public ResolvedTypeReference? Type { get; private set; }

    public ParameterSymbol(string name, Symbol parentSymbol, SyntaxNode declaration, int ordinal)
        : base(SymbolKind.Parameter, name, parentSymbol, declaration)
    {
        Ordinal = ordinal;
    }

    public void ResolveType(ResolvedTypeReference type)
    {
        Type = type;
    }
}
