using Maho.Text;

namespace Maho.Syntax;

/// <summary> Represents whitespace or comment trivia attached to tokens. </summary>
internal readonly struct SyntaxTrivia(string text, SyntaxTriviaKind kind, TextSpan span)
{
    /// <summary> The text content of the trivia. </summary>
    public string Text { get; } = text;
    
    /// <summary> The kind of trivia. </summary>
    public SyntaxTriviaKind Kind { get; } = kind;
    
    /// <summary> The position of the trivia in the source text. </summary>
    public TextSpan Span { get; } = span;
}

/// <summary> Defines the different kinds of trivia that can be attached to tokens. </summary>
internal enum SyntaxTriviaKind
{
    Whitespace,
    EndOfLine,
    SingleLineComment,
    MultiLineComment
}
