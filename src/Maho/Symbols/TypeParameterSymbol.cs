using Maho.Syntax;

namespace Maho.Symbols;

internal sealed class TypeParameterSymbol : DeclaredSymbol
{
    public int Ordinal { get; }
    public string SignatureIdentity { get; }

    public TypeParameterSymbol(string name, Symbol parentSymbol, SyntaxNode declaration, int ordinal)
        : base(SymbolKind.TypeParameter, name, parentSymbol, declaration)
    {
        Ordinal = ordinal;
        SignatureIdentity = parentSymbol switch
        {
            FunctionSymbol => $"!!{ordinal}",
            TypeSymbol typeSymbol => $"!{typeSymbol.QualifiedMetadataName}:{ordinal}",
            _ => $"!{parentSymbol.QualifiedMetadataName}:{ordinal}"
        };
    }
}