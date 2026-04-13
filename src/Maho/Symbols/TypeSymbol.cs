using Maho.Syntax;

namespace Maho.Symbols;

/// <summary> Semantic symbol for a declared type such as a class, struct, enum, or interface. </summary>
internal sealed class TypeSymbol : DeclaredSymbol
{
    /// <summary> Generic type parameters declared directly on this type, in source order. </summary>
    public TypeParameterSymbol[] TypeParameters { get; private set; } = [];
    /// <summary> Directly declared hierarchy edges after type-hierarchy resolution has completed. </summary>
    public TypeSymbol[] BaseTypes { get; private set; } = [];
    /// <summary> Cached metadata name including generic arity suffix when applicable. </summary>
    private string? metadataName;

    /// <summary> Generic arity declared on the type name. </summary>
    public int Arity { get; }
    /// <summary> Metadata-facing type name, including the CLR-style arity suffix for generics. </summary>
    public override string MetadataName => metadataName ??= CreateMetadataName();
    /// <summary> Stable declaration key used to compare type declarations in one scope. </summary>
    public TypeDeclarationKey DeclarationKey { get; }

    /// <summary> Creates one declared type symbol and its declaration key. </summary>
    public TypeSymbol(SymbolName name, Symbol parentSymbol, SyntaxNode declaration, int arity)
        : base(SymbolKind.Type, name, parentSymbol, declaration)
    {
        Arity = arity;
        DeclarationKey = new TypeDeclarationKey(name, arity);
    }

    /// <summary> Records the resolved generic type parameters once symbol discovery has created them. </summary>
    public void ResolveTypeParameters(TypeParameterSymbol[] resolvedTypeParameters) => TypeParameters = resolvedTypeParameters;

    /// <summary> Records the directly declared hierarchy edges once type-hierarchy resolution has completed. </summary>
    public void ResolveBaseTypes(TypeSymbol[] resolvedBaseTypes) => BaseTypes = resolvedBaseTypes;

    /// <summary> Builds the metadata-visible name lazily so analysis only pays for it on demand. </summary>
    private string CreateMetadataName() => Arity == 0 ? Name.ToString() : $"{Name}`{Arity}";
}
