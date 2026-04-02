using System;
using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

internal abstract class ResolvedTypeReference
{
    protected ResolvedTypeReference(TypeSyntax syntax, IReadOnlyList<Symbol> candidateSymbols)
    {
        Syntax = syntax;
        CandidateSymbols = candidateSymbols;
    }

    public TypeSyntax Syntax { get; }
    public IReadOnlyList<Symbol> CandidateSymbols { get; }
    public abstract string DisplayName { get; }
    public abstract string SignatureKey { get; }
}

internal sealed class ResolvedNamedTypeReference : ResolvedTypeReference
{
    public string Name { get; }
    public int Arity { get; }
    public IReadOnlyList<ResolvedTypeReference> TypeArguments { get; }
    public override string DisplayName { get; }
    public override string SignatureKey { get; }

    public ResolvedNamedTypeReference(
        TypeSyntax syntax,
        string name,
        int arity,
        IReadOnlyList<ResolvedTypeReference> typeArguments,
        IReadOnlyList<Symbol> candidateSymbols,
        string? signatureIdentity = null)
        : base(syntax, candidateSymbols)
    {
        Name = name;
        Arity = arity;
        TypeArguments = typeArguments;
        DisplayName = CreateDisplayName(name, typeArguments);
        SignatureKey = signatureIdentity ?? CreateSignatureKey(name, arity, typeArguments);
    }

    private static string CreateDisplayName(string name, IReadOnlyList<ResolvedTypeReference> typeArguments)
    {
        if (typeArguments.Count == 0)
            return name;

        string[] parts = new string[typeArguments.Count];

        for (int i = 0; i < parts.Length; i++)
            parts[i] = typeArguments[i].DisplayName;

        return $"{name}<{string.Join(", ", parts)}>";
    }

    private static string CreateSignatureKey(string name, int arity, IReadOnlyList<ResolvedTypeReference> typeArguments)
    {
        if (typeArguments.Count == 0)
            return arity == 0 ? name : $"{name}`{arity}";

        string[] parts = new string[typeArguments.Count];

        for (int i = 0; i < parts.Length; i++)
            parts[i] = typeArguments[i].SignatureKey;

        return $"{name}`{arity}<{string.Join(",", parts)}>";
    }
}

internal sealed class ResolvedQualifiedTypeReference : ResolvedTypeReference
{
    public ResolvedTypeReference Left { get; }
    public ResolvedTypeReference Right { get; }
    public override string DisplayName { get; }
    public override string SignatureKey { get; }

    public ResolvedQualifiedTypeReference(
        QualifiedType syntax,
        ResolvedTypeReference left,
        ResolvedTypeReference right,
        IReadOnlyList<Symbol> candidateSymbols)
        : base(syntax, candidateSymbols)
    {
        Left = left;
        Right = right;
        DisplayName = $"{left.DisplayName}.{right.DisplayName}";
        SignatureKey = BuildSignatureKey(candidateSymbols, left, right);
    }

    private static string BuildSignatureKey(IReadOnlyList<Symbol> candidates, ResolvedTypeReference left, ResolvedTypeReference right)
    {
        if (candidates.Count == 1)
            return candidates[0].QualifiedMetadataName;

        return $"{left.SignatureKey}.{right.SignatureKey}";
    }
}

internal sealed class ResolvedModifiedTypeReference : ResolvedTypeReference
{
    public ResolvedTypeReference ElementType { get; }
    public PostfixTypeModifier Modifier { get; }
    public override string DisplayName { get; }
    public override string SignatureKey { get; }

    public ResolvedModifiedTypeReference(ModifiedType syntax, ResolvedTypeReference elementType)
        : base(syntax, [])
    {
        ElementType = elementType;
        Modifier = syntax.Modifier ?? throw new InvalidOperationException("ModifiedType is missing a postfix modifier.");
        string suffix = GetModifierSuffix(Modifier);
        DisplayName = $"{elementType.DisplayName}{suffix}";
        SignatureKey = $"{elementType.SignatureKey}{suffix}";
    }

    private static string GetModifierSuffix(PostfixTypeModifier modifier) => modifier switch
    {
        ArrayTypeModifier arrayTypeModifier when arrayTypeModifier.Size is null => "[]",
        ArrayTypeModifier => "[expr]",
        OptionalTypeModifier => "?",
        PointerTypeModifier => "*",
        ReferenceTypeModifier => "&",
        _ => throw new InvalidOperationException($"Unhandled postfix modifier '{modifier.GetType().Name}'.")
    };
}
