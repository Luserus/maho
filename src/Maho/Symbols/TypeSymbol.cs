using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Symbols;

internal sealed class TypeSymbol : DeclaredSymbol
{
    private IReadOnlyList<TypeParameterSymbol> typeParameters = [];

    public int Arity { get; }
    public override string MetadataName => Arity == 0 ? Name.ToString() : $"{Name}`{Arity}";
    public TypeDeclarationKey DeclarationKey { get; }
    public IReadOnlyList<TypeParameterSymbol> TypeParameters => typeParameters;

    public TypeSymbol(SymbolName name, Symbol parentSymbol, SyntaxNode declaration, int arity)
        : base(SymbolKind.Type, name, parentSymbol, declaration)
    {
        Arity = arity;
        DeclarationKey = new TypeDeclarationKey(name, arity);
    }

    public void ResolveTypeParameters(IReadOnlyList<TypeParameterSymbol> resolvedTypeParameters) => typeParameters = resolvedTypeParameters;
}
