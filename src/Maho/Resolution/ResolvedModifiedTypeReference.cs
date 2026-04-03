using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Represents a type reference with a postfix modifier such as <c>[]</c>, <c>*</c>, <c>&amp;</c>, or <c>?</c>. </summary>
internal sealed class ResolvedModifiedTypeReference : ResolvedTypeReference
{
    public ResolvedTypeReference ElementType { get; }
    public PostfixTypeModifier Modifier { get; }
    public override string DisplayName { get; }
    public override string SignatureKey { get; }

    public ResolvedModifiedTypeReference(ModifiedType syntax, ResolvedTypeReference elementType, PostfixTypeModifier modifier)
        : base(syntax, [])
    {
        ElementType = elementType;
        Modifier = modifier;
        string suffix = GetModifierSuffix(modifier);
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
        _ => throw new System.InvalidOperationException($"Unhandled postfix modifier '{modifier.GetType().Name}'.")
    };
}
