using System.Collections.Generic;
using Maho.Syntax;
using Maho.Resolution;

namespace Maho.Symbols;

internal sealed class FunctionSymbol : DeclaredSymbol
{
    private IReadOnlyList<TypeParameterSymbol> typeParameters = [];
    private IReadOnlyList<ParameterSymbol> parameters = [];

    public int Arity { get; }
    public override string MetadataName => Arity == 0 ? Name : $"{Name}``{Arity}";
    public ResolvedTypeReference? ReturnType { get; private set; }
    public FunctionDeclarationKey? DeclarationKey { get; private set; }
    public int ParameterCount => parameters.Count;
    public string ParameterSignatureKey { get; private set; } = "()";
    public IReadOnlyList<TypeParameterSymbol> TypeParameters => typeParameters;
    public IReadOnlyList<ParameterSymbol> Parameters => parameters;

    public FunctionSymbol(string name, Symbol parentSymbol, SyntaxNode declaration, int arity)
        : base(SymbolKind.Function, name, parentSymbol, declaration)
    {
        Arity = arity;
    }

    public void ResolveTypeParameters(IReadOnlyList<TypeParameterSymbol> resolvedTypeParameters)
    {
        typeParameters = resolvedTypeParameters;
    }

    public void ResolveParameters(IReadOnlyList<ParameterSymbol> resolvedParameters)
    {
        parameters = resolvedParameters;
    }

    public void ResolveSignature(ResolvedTypeReference returnType)
    {
        ReturnType = returnType;
        ParameterSignatureKey = BuildParameterSignatureKey(parameters);
        DeclarationKey = new FunctionDeclarationKey(Name, Arity, ParameterSignatureKey);
    }

    private static string BuildParameterSignatureKey(IReadOnlyList<ParameterSymbol> parameters)
    {
        if (parameters.Count == 0)
            return "()";

        string[] parts = new string[parameters.Count];

        for (int i = 0; i < parameters.Count; i++)
        {
            ResolvedTypeReference? parameterType = parameters[i].Type;
            parts[i] = parameterType is null ? "?" : parameterType.SignatureKey;
        }

        return $"({string.Join(",", parts)})";
    }
}
