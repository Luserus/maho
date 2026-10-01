using System;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Token of the program which serves as the smallest unit of meaningful data the compiler can use. </summary>
internal sealed class Token : SyntaxNode
{
    internal SourceText Source { get; }
    /// <summary> Token data of the Token. </summary>
    public string Value => Source.ToString(Span);
    /// <summary> Text span of the Token. </summary>
    public TextSpan Span { get; set; }
    /// <summary> Token kind of the Token. </summary>
    public TokenKind Kind { get; set; }
    /// <summary> Leading trivia of the Token. </summary>
    public SyntaxTrivia[] LeadingTrivia { get; set; }
    /// <summary> Trailing trivia of the Token. </summary>
    public SyntaxTrivia[] TrailingTrivia { get; set; }
    public MatchingKeywordKind MatchingKind { get; }

    /// <summary> Whether this token is a suffixed literal. </summary>
    public bool HasSuffix => Kind is TokenKind.SuffixedInteger or TokenKind.SuffixedFloat or TokenKind.SuffixedChar or TokenKind.SuffixedString;

    /// <summary> Gets the span of the suffix text within the token, or an empty span if not a suffixed literal. </summary>
    public ReadOnlySpan<char> GetSuffixSpan()
    {
        if (!HasSuffix)
            return [];

        ReadOnlySpan<char> text = Source.AsSpan(Span);

        if (Kind is TokenKind.SuffixedChar or TokenKind.SuffixedString)
        {
            char quote = Kind is TokenKind.SuffixedChar ? '\'' : '"';
            int lastQuote = text.LastIndexOf(quote);

            if (lastQuote >= 0 && lastQuote + 1 < text.Length)
                return text[(lastQuote + 1)..];

            return [];
        }

        // For SuffixedInteger and SuffixedFloat:
        if (text.Length >= 3 && text[0] == '0' && (text[1] is 'b' or 'B') && (text[2] is '0' or '1' || (text[2] == '_' && text.Length >= 4 && text[3] is '0' or '1')))
        {
            int i = 2;

            while (i < text.Length && (text[i] is '0' or '1' or '_'))
                i++;

            return text[i..];
        }

        if (text.Length >= 3 && text[0] == '0' && (text[1] is 'x' or 'X') && (char.IsAsciiHexDigit(text[2]) || (text[2] == '_' && text.Length >= 4 && char.IsAsciiHexDigit(text[3]))))
        {
            int i = 2;

            while (i < text.Length && (char.IsAsciiHexDigit(text[i]) || text[i] == '_'))
                i++;

            return text[i..];
        }

        // Decimal integer or float:
        {
            int i = 0;

            while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '_'))
                i++;

            if (i < text.Length && text[i] == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
            {
                i++; // skip '.'
                while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '_'))
                    i++;
            }

            if (i < text.Length && (text[i] is 'e' or 'E'))
            {
                int next = i + 1;

                if (next < text.Length && (text[next] is '+' or '-'))
                    next++;

                if (next < text.Length && (char.IsAsciiDigit(text[next]) || (text[next] == '_' && next + 1 < text.Length && char.IsAsciiDigit(text[next + 1]))))
                {
                    i = next;

                    while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '_'))
                        i++;
                }
            }

            return text[i..];
        }
    }

    /// <summary> Suffix attached to the literal token, or <c>null</c> if none. </summary>
    public string? Suffix
    {
        get
        {
            var suffixSpan = GetSuffixSpan();
            return suffixSpan.IsEmpty ? null : suffixSpan.ToString();
        }
    }

    /// <summary> Token text without the suffix (and stripping any separator underscore before the suffix for numbers). </summary>
    public string ValueWithoutSuffix
    {
        get
        {
            var suffixSpan = GetSuffixSpan();

            if (suffixSpan.IsEmpty)
                return Value;

            var val = Value;
            var without = val[..^suffixSpan.Length];

            if (Kind is TokenKind.SuffixedInteger or TokenKind.SuffixedFloat)
                without = without.TrimEnd('_');

            return without;
        }
    }

    /// <summary> Initializes the Token struct. </summary>
    /// <param name="sourceText"> Source text of the Token. </param>
    /// <param name="span"> Text span of the Token. </param>
    /// <param name="kind"> Token kind of the Token. </param>
    /// <param name="leadingTrivia"> Leading trivia of the Token. </param>
    /// <param name="trailingTrivia"> Trailing trivia of the Token. </param>
    public Token(SourceText sourceText, TextSpan span, TokenKind kind, SyntaxTrivia[] leadingTrivia, SyntaxTrivia[] trailingTrivia)
        : this(sourceText, span, kind, leadingTrivia, trailingTrivia, MatchingKeywordKind.None) { }

    public Token(SourceText sourceText, TextSpan span, TokenKind kind, SyntaxTrivia[] leadingTrivia, SyntaxTrivia[] trailingTrivia, MatchingKeywordKind matchingKind)
    {
        Source = sourceText;
        Span = span;
        Kind = kind;
        LeadingTrivia = leadingTrivia;
        TrailingTrivia = trailingTrivia;
        MatchingKind = matchingKind;
    }
}