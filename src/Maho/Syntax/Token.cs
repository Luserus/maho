using Maho.Text;

namespace Maho.Syntax;

/// <summary> Token of the program which serves as the smallest unit of meaningful data the compiler can use. </summary>
internal sealed class Token : SyntaxNode
{
    private readonly SourceText source;
    /// <summary> Token data of the Token. </summary>
    public string Value => source.ToString(Span);
    /// <summary> Text span of the Token. </summary>
    public TextSpan Span { get; set; }
    /// <summary> Token kind of the Token. </summary>
    public TokenKind Kind { get; set; }
    /// <summary> Leading trivia of the Token. </summary>
    public SyntaxTrivia[] LeadingTrivia { get; set; }
    /// <summary> Trailing trivia of the Token. </summary>
    public SyntaxTrivia[] TrailingTrivia { get; set; }
    public MatchingKeywordKind MatchingKind { get; }

    /// <summary> Initializes the Token struct. </summary>
    /// <param name="value"> The Token data in string form. </param>
    /// <param name="span"> Text span of the Token. </param>
    /// <param name="kind"> Token kind of the Token. </param>
    /// <param name="leadingTrivia"> Leading trivia of the Token. </param>
    /// <param name="trailingTrivia"> Trailing trivia of the Token. </param>
    public Token(SourceText sourceText, TextSpan span, TokenKind kind, SyntaxTrivia[] leadingTrivia, SyntaxTrivia[] trailingTrivia) : this(sourceText, span, kind, leadingTrivia, trailingTrivia, MatchingKeywordKind.None) {}
    public Token(SourceText sourceText, TextSpan span, TokenKind kind, SyntaxTrivia[] leadingTrivia, SyntaxTrivia[] trailingTrivia, MatchingKeywordKind matchingKind)
    {
        source = sourceText;
        Span = span;
        Kind = kind;
        LeadingTrivia = leadingTrivia;
        TrailingTrivia = trailingTrivia;
        MatchingKind = matchingKind;
    }
}