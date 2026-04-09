using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Represents a type reference with a postfix modifier such as <c>[]</c>, <c>*</c>, <c>&amp;</c>, or <c>?</c>. </summary>
internal sealed class ResolvedModifiedTypeReference : ResolvedTypeReference
{
    /// <summary> Normalized postfix suffix reused by both display and signature formatting. </summary>
    private readonly string modifierSuffix;
    /// <summary> Cached display name built only if a consumer needs the human-readable form. </summary>
    private string? displayName;
    /// <summary> Cached signature key built only if a semantic comparison actually needs it. </summary>
    private string? signatureKey;

    /// <summary> Underlying element/reference target before the postfix modifier was applied. </summary>
    public ResolvedTypeReference ElementType { get; }
    /// <summary> The postfix modifier syntax itself, such as array, pointer, reference, or optional. </summary>
    public PostfixTypeModifier Modifier { get; }
    /// <summary> Human-readable display form including the rendered postfix suffix. </summary>
    public override string DisplayName => displayName ??= $"{ElementType.DisplayName}{modifierSuffix}";
    /// <summary> Stable signature form including the normalized postfix suffix. </summary>
    public override string SignatureKey => signatureKey ??= $"{ElementType.SignatureKey}{modifierSuffix}";

    /// <summary> Creates a semantic model for a modified type such as <c>T[]</c> or <c>T?</c>. </summary>
    public ResolvedModifiedTypeReference(ModifiedType syntax, ResolvedTypeReference elementType, PostfixTypeModifier modifier)
        : base(syntax, [])
    {
        ElementType = elementType;
        Modifier = modifier;
        modifierSuffix = GetModifierSuffix(modifier);
    }

    /// <summary> Normalizes every supported postfix modifier into the suffix used by display/signature output. </summary>
    private static string GetModifierSuffix(PostfixTypeModifier modifier) => modifier switch
    {
        ArrayTypeModifier arrayTypeModifier when arrayTypeModifier.Size is null => "[]",
        ArrayTypeModifier => "[expr]",
        OptionalTypeModifier => "?",
        PointerTypeModifier => "*",
        ReferenceTypeModifier => "&",
        _ => throw new System.InvalidOperationException($"Unhandled postfix modifier '{modifier.GetType().Name}'.")
    };
}
