using Maho.Text;

namespace Maho.Syntax;

/// <summary> Represents whitespace or comment trivia attached to tokens. </summary>
internal readonly struct SyntaxTrivia
{
    /// <summary> The text content of the trivia. </summary>
    public string Text { get; }
    /// <summary> The kind of trivia. </summary>
    public SyntaxTriviaKind Kind { get; }
    /// <summary> The position of the trivia in the source text. </summary>
    public TextSpan Span { get; }

    /// <summary> Initializes the SyntaxTrivia struct. </summary>
    /// <param name="text"> The text content of the trivia. </param>
    /// <param name="kind"> The kind of trivia. </param>
    /// <param name="span"> The position of the trivia in the source text. </param>
    public SyntaxTrivia(string text, SyntaxTriviaKind kind, TextSpan span)
    {
        Text = text;
        Kind = kind;
        Span = span;
    }
}
