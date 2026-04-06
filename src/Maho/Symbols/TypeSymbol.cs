using System;
using Maho.Syntax;

namespace Maho.Symbols;

/// <summary> Semantic symbol for a declared type such as a class, struct, enum, or interface. </summary>
internal sealed class TypeSymbol : DeclaredSymbol
{
    /// <summary> Generic type parameters declared directly on this type, in source order. </summary>
    private TypeParameterSymbol[] typeParameters = [];
    /// <summary> Cached metadata name including generic arity suffix when applicable. </summary>
    private string? metadataName;

    /// <summary> Generic arity declared on the type name. </summary>
    public int Arity { get; }
    /// <summary> Metadata-facing type name, including the CLR-style arity suffix for generics. </summary>
    public override string MetadataName => metadataName ??= CreateMetadataName();
    /// <summary> Stable declaration key used to compare type declarations in one scope. </summary>
    public TypeDeclarationKey DeclarationKey { get; }
    /// <summary> Declared generic parameters attached to this type. </summary>
    public ReadOnlySpan<TypeParameterSymbol> TypeParameters => typeParameters;

    /// <summary> Creates one declared type symbol and its declaration key. </summary>
    public TypeSymbol(SymbolName name, Symbol parentSymbol, SyntaxNode declaration, int arity)
        : base(SymbolKind.Type, name, parentSymbol, declaration)
    {
        Arity = arity;
        DeclarationKey = new TypeDeclarationKey(name, arity);
    }

    /// <summary> Records the resolved generic type parameters once symbol discovery has created them. </summary>
    public void ResolveTypeParameters(TypeParameterSymbol[] resolvedTypeParameters) => typeParameters = resolvedTypeParameters;

    /// <summary> Builds the metadata-visible name lazily so analysis only pays for it on demand. </summary>
    private string CreateMetadataName() => Arity == 0 ? Name.ToString() : $"{Name}`{Arity}";
}
