namespace Maho.Syntax;

/// <summary> Defines the different kinds of trivia that can be attached to tokens. </summary>
internal enum SyntaxTriviaKind
{
    Whitespace,
    EndOfLine,
    SingleLineComment,
    MultiLineComment
}
