namespace Maho.Syntax;

internal sealed class OptionalTypeModifier : PostfixTypeModifier
{
    public Token QuestionMark { get; }
    public PostfixTypeModifierKind Kind => PostfixTypeModifierKind.Optional;

    public OptionalTypeModifier(Token questionMark) => QuestionMark = questionMark;
}