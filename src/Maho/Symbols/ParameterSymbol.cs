using Maho.Syntax;
using Maho.Resolution;

namespace Maho.Symbols;

/// <summary> Semantic symbol for one parameter declared by a function signature. </summary>
internal sealed class ParameterSymbol : DeclaredSymbol
{
    /// <summary> Zero-based ordinal of the parameter within its declaring function. </summary>
    public int Ordinal { get; }
    /// <summary> Resolved semantic type assigned to the parameter once type binding has run. </summary>
    public ResolvedTypeReference? Type { get; private set; }

    /// <summary> Creates one declared parameter symbol. </summary>
    public ParameterSymbol(SymbolName name, Symbol parentSymbol, SyntaxNode declaration, int ordinal)
        : base(SymbolKind.Parameter, name, parentSymbol, declaration) => Ordinal = ordinal;

    /// <summary> Records the resolved parameter type. </summary>
    public void ResolveType(ResolvedTypeReference type) => Type = type;
}
