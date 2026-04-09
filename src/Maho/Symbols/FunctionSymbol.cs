using System;
using Maho.Syntax;
using Maho.Resolution;

namespace Maho.Symbols;

/// <summary> Semantic symbol for a declared function, including generics, parameters, and return type. </summary>
internal sealed class FunctionSymbol : DeclaredSymbol
{
    /// <summary> Generic type parameters declared directly on the function signature. </summary>
    private TypeParameterSymbol[] typeParameters = [];
    /// <summary> Parameters declared by the function signature in source order. </summary>
    private ParameterSymbol[] parameters = [];
    /// <summary> Cached metadata name including generic arity suffix when applicable. </summary>
    private string? metadataName;
    /// <summary> Cached normalized parameter signature string built only if a consumer asks for it. </summary>
    private string? parameterSignatureKey;
    /// <summary> Lazily materialized declaration key that combines name, arity, and parameter shape. </summary>
    private FunctionDeclarationKey? declarationKey;

    /// <summary> Generic arity declared on the function name. </summary>
    public int Arity { get; }
    /// <summary> Metadata-facing function name, including the CLR-style method arity suffix. </summary>
    public override string MetadataName => metadataName ??= CreateMetadataName();
    /// <summary> Resolved return type once later semantic passes fill it in. </summary>
    public ResolvedTypeReference? ReturnType { get; private set; }
    /// <summary> Stable declaration key exposed once the signature has been resolved. </summary>
    public FunctionDeclarationKey? DeclarationKey =>
        ReturnType is null ? null : declarationKey ??= new FunctionDeclarationKey(Name, Arity, ParameterSignatureKey);
    /// <summary> Number of declared parameters without forcing signature materialization. </summary>
    public int ParameterCount => parameters.Length;
    /// <summary> Normalized parameter signature used by overload/declaration identity logic. </summary>
    public string ParameterSignatureKey => parameterSignatureKey ??= BuildParameterSignatureKey(parameters);
    /// <summary> Declared generic parameters attached to this function. </summary>
    public ReadOnlySpan<TypeParameterSymbol> TypeParameters => typeParameters;
    /// <summary> Declared parameters attached to this function. </summary>
    public ReadOnlySpan<ParameterSymbol> Parameters => parameters;

    /// <summary> Creates one declared function symbol. </summary>
    public FunctionSymbol(SymbolName name, Symbol parentSymbol, SyntaxNode declaration, int arity)
        : base(SymbolKind.Function, name, parentSymbol, declaration) => Arity = arity;

    /// <summary> Records the resolved generic type parameters once symbol discovery has created them. </summary>
    public void ResolveTypeParameters(TypeParameterSymbol[] resolvedTypeParameters) => typeParameters = resolvedTypeParameters;

    /// <summary> Records the resolved parameters once symbol discovery has created them. </summary>
    public void ResolveParameters(ParameterSymbol[] resolvedParameters) => parameters = resolvedParameters;

    /// <summary>
    /// Records the resolved return type and clears any cached declaration identity so later
    /// consumers see a signature key consistent with the latest semantic state.
    /// </summary>
    public void ResolveSignature(ResolvedTypeReference returnType)
    {
        ReturnType = returnType;
        parameterSignatureKey = null;
        declarationKey = null;
    }

    /// <summary> Builds the normalized parameter signature string used by declaration identity. </summary>
    private static string BuildParameterSignatureKey(ReadOnlySpan<ParameterSymbol> parameters)
    {
        if (parameters.Length == 0)
            return "()";

        string[] parts = new string[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            ResolvedTypeReference? parameterType = parameters[i].Type;
            parts[i] = parameterType is null ? "?" : parameterType.SignatureKey;
        }

        return $"({string.Join(",", parts)})";
    }

    /// <summary> Builds the metadata-visible function name lazily so semantic discovery avoids eager formatting. </summary>
    private string CreateMetadataName() => Arity == 0 ? Name.ToString() : $"{Name}``{Arity}";
}
