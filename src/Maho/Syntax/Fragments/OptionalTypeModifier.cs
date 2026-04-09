namespace Maho.Syntax;

/// <summary> Optional postfix type modifier. </summary>
internal sealed class OptionalTypeModifier : PostfixTypeModifier
{
    /// <summary> Question mark token. </summary>
    public Token QuestionMark { get; }
    /// <summary> Modifier kind value. </summary>
    public PostfixTypeModifierKind Kind => PostfixTypeModifierKind.Optional;

    /// <summary> Creates one optional modifier node. </summary>
    public OptionalTypeModifier(Token questionMark) => QuestionMark = questionMark;
}
