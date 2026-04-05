using Maho.Syntax;

namespace Maho.Symbols;

/// <summary> Semantic symbol for a generic type parameter declared on a type or function. </summary>
internal sealed class TypeParameterSymbol : DeclaredSymbol
{
    /// <summary> Cached normalized identity used when later semantic stages need a stable type-parameter key. </summary>
    private string? signatureIdentity;

    /// <summary> Zero-based ordinal of the type parameter within its declaring symbol. </summary>
    public int Ordinal { get; }
    /// <summary> Stable signature identity built only if a later consumer asks for it. </summary>
    public string SignatureIdentity => signatureIdentity ??= CreateSignatureIdentity();

    /// <summary> Creates one declared type-parameter symbol. </summary>
    public TypeParameterSymbol(SymbolName name, Symbol parentSymbol, SyntaxNode declaration, int ordinal)
        : base(SymbolKind.TypeParameter, name, parentSymbol, declaration) => Ordinal = ordinal;

    /// <summary>
    /// Builds the normalized type-parameter identity. Function type parameters are scoped only by
    /// ordinal today, while type-owned parameters include the containing metadata name.
    /// </summary>
    private string CreateSignatureIdentity() => ParentSymbol switch
    {
        FunctionSymbol => $"!!{Ordinal}",
        TypeSymbol typeSymbol => $"!{typeSymbol.QualifiedMetadataName}:{Ordinal}",
        Symbol parentSymbol => $"!{parentSymbol.QualifiedMetadataName}:{Ordinal}",
        _ => $"!!{Ordinal}"
    };
}
