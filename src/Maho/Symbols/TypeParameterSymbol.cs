using Maho.Syntax;

namespace Maho.Symbols;

internal sealed class TypeParameterSymbol : DeclaredSymbol
{
    private string? signatureIdentity;

    public int Ordinal { get; }
    public string SignatureIdentity => signatureIdentity ??= CreateSignatureIdentity();

    public TypeParameterSymbol(SymbolName name, Symbol parentSymbol, SyntaxNode declaration, int ordinal)
        : base(SymbolKind.TypeParameter, name, parentSymbol, declaration) => Ordinal = ordinal;

    private string CreateSignatureIdentity() => ParentSymbol switch
    {
        FunctionSymbol => $"!!{Ordinal}",
        TypeSymbol typeSymbol => $"!{typeSymbol.QualifiedMetadataName}:{Ordinal}",
        Symbol parentSymbol => $"!{parentSymbol.QualifiedMetadataName}:{Ordinal}",
        _ => $"!!{Ordinal}"
    };
}
