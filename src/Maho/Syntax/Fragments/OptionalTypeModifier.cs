namespace Maho.Syntax;

/// <summary> Optional postfix type modifier. </summary>
internal sealed class OptionalTypeModifier : PostfixTypeModifier
{
    /// <summary> Question mark token. </summary>
    public Token QuestionMark { get; }

    /// <summary> Creates one optional modifier node. </summary>
    public OptionalTypeModifier(Token questionMark) : base(PostfixTypeModifierKind.Optional) => QuestionMark = questionMark;
}
