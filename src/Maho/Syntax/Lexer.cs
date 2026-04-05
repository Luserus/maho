using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Maho.Diagnostics;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Lexes the program string into tokens which is later passed to the Parser for syntactic analysis. </summary>
internal sealed partial class Lexer
{
    /// <summary> Shared sink used to report lexer diagnostics against the current source buffer. </summary>
    private readonly DiagnosticsManager diagnostics;
    /// <summary> Current index of char being read from the program string. </summary>
    private int current;
    /// <summary> The source text of the program. </summary>
    private readonly SourceText text;

    /// <summary> Character currently under the lexer cursor, or <c>'\0'</c> once the cursor moves past the end. </summary>
    private char CurrentChar => current >= text.Length ? '\0' : text[current];

    /// <summary> Tokens lexed by the Lexer. </summary>
    public List<Token> Tokens { get; } = new List<Token>(256);

    /// <summary> Initializes a new instance of the Lexer class. </summary>
    /// <param name="sourceText"> Source text of the program. </param>
    /// <param name="diagnosticsManager"> Shared diagnostics sink for invalid tokens and recovery messages. </param>
    public Lexer(SourceText sourceText, DiagnosticsManager diagnosticsManager)
    {
        text = sourceText;
        diagnostics = diagnosticsManager;
    }

    /// <summary> Lexes the program string into tokens with trivia. </summary>
    public List<Token> Lex()
    {
        while (current < text.Length)
        {
            var leadingTrivia = LexTrivia();
            var (span, kind) = LexTokenData();
            var trailingTrivia = LexTrivia();
            var matching = MatchingKeywordKind.None;

            if (kind is TokenKind.Identifier)
                matching = MatchKeywordKind(span);

            Tokens.Add(new(text, span, kind, leadingTrivia, trailingTrivia, matching));
        }

        // Add an EndToken at the end of the list to tell the parser when the final token has been reached.
        Tokens.Add(new(text, new TextSpan(text.Length, 0), TokenKind.EndToken, [], []));

        return Tokens;
    }

    /// <summary> Current token kind. </summary>
    private TokenKind kind = TokenKind.NullToken;

    /// <summary> Lexes a part of the program and returns the required token data. </summary>
    /// <returns> The token data for creating a token. </returns>
    private (TextSpan Span, TokenKind kind) LexTokenData()
    {
        var start = current;

        if (char.IsLetter(CurrentChar) || CurrentChar == '_')
        {
            kind = TokenKind.Identifier;

            while (char.IsLetterOrDigit(Peek(0)) || CurrentChar == '_')
                current++;
        }
        else if (char.IsAsciiDigit(CurrentChar))
        {
            kind = TokenKind.Integer;

            while (char.IsAsciiDigit(Peek(0)))
                current++;

            if (IsOperator(CurrentChar) is (true, TokenKind.Dot) && char.IsAsciiDigit(Peek()))
            {
                kind = TokenKind.Float;
                current++;

                while (char.IsAsciiDigit(Peek(0)))
                    current++;
            }
        }
        else if (IsOperator(CurrentChar) is (true, var opKind))
        {
            if (opKind is TokenKind.SingleQuote)
                return LexQuotedLiteral(start, '\'', TokenKind.Char);
            else if (opKind is TokenKind.DoubleQuote)
                return LexQuotedLiteral(start, '"', TokenKind.String);
            else if (opKind is TokenKind.Dot && char.IsAsciiDigit(Peek()))
            {
                kind = TokenKind.Float;
                current++;

                while (char.IsAsciiDigit(Peek(0)))
                    current++;
            }
            else
            {
                kind = opKind;
                current++;
            }
        }
        else
        {
            kind = TokenKind.BadToken;
            ReportBadToken(start);
            current++;
        }

        TextSpan span = new(start, current - start);

        return (span, kind);
    }

    /// <summary>
    /// Lexes a quoted literal until the matching terminator, tracking character payload length so
    /// the lexer can diagnose malformed character literals without fully interpreting escapes.
    /// </summary>
    /// <param name="start">Source offset where the opening quote was seen.</param>
    /// <param name="terminator">Expected closing quote character.</param>
    /// <param name="tokenKind">Token kind to produce for the literal.</param>
    /// <returns>The captured literal span together with the requested token kind.</returns>
    private (TextSpan Span, TokenKind Kind) LexQuotedLiteral(int start, char terminator, TokenKind tokenKind)
    {
        kind = tokenKind;
        current++; // opening quote
        int characterCount = 0;

        while (true)
        {
            if (CurrentChar == '\0')
            {
                ReportUnterminatedLiteral(start, tokenKind);
                return (new TextSpan(start, current - start), tokenKind);
            }

            if (CurrentChar is '\r' or '\n')
            {
                ReportUnterminatedLiteral(start, tokenKind);
                return (new TextSpan(start, current - start), tokenKind);
            }

            if (CurrentChar == terminator)
            {
                current++;

                if (tokenKind is TokenKind.Char)
                    ReportCharacterLiteralLength(start, characterCount);

                return (new TextSpan(start, current - start), tokenKind);
            }

            if (CurrentChar == '\\')
            {
                current++;

                if (CurrentChar == '\0' || CurrentChar is '\r' or '\n')
                {
                    ReportUnterminatedLiteral(start, tokenKind);
                    return (new TextSpan(start, current - start), tokenKind);
                }

                current++;
                characterCount++;
                continue;
            }

            current++;
            characterCount++;
        }
    }

    /// <summary> Lexes a part of the program and returns all leading/trailing trivias before/after a token. </summary>
    /// <returns> The trivias as an array. </returns>
    private SyntaxTrivia[] LexTrivia()
    {
        List<SyntaxTrivia> trivias = [];
        var tokenKind = kind;

        while (current < text.Length)
        {
            SyntaxTriviaKind kind;
            var start = current;

            if (CurrentChar == ' ')
            {
                current++;
                kind = SyntaxTriviaKind.Whitespace;
                tokenKind = TokenKind.Whitespace;

                while (CurrentChar == ' ')
                    current++;

                trivias.Add(new(kind, new TextSpan(start, current - start)));
            }
            else if (CurrentChar == '\t')
            {
                current++;
                kind = SyntaxTriviaKind.Whitespace;
                tokenKind = TokenKind.Tabspace;

                while (CurrentChar == '\t')
                    current++;

                trivias.Add(new(kind, new TextSpan(start, current - start)));
            }
            else if (CurrentChar == '\n')
            {
                current++;
                kind = SyntaxTriviaKind.EndOfLine;
                tokenKind = TokenKind.Newline;

                trivias.Add(new(kind, new TextSpan(start, current - start)));
            }
            else
                break;
        }

        kind = tokenKind;
        return [.. trivias];
    }

    /// <summary> Maps an identifier span to its contextual keyword classification without allocating a managed string. </summary>
    /// <param name="span">Identifier source span to classify.</param>
    /// <returns>The matched contextual keyword kind, or <see cref="MatchingKeywordKind.None"/>.</returns>
    private MatchingKeywordKind MatchKeywordKind(TextSpan span)
    {
        ReadOnlySpan<char> identifier = text.AsSpan(span);

        return identifier.Length switch
        {
            2 when identifier.SequenceEqual("if") => MatchingKeywordKind.If,
            3 when identifier.SequenceEqual("for") => MatchingKeywordKind.For,
            3 when identifier.SequenceEqual("new") => MatchingKeywordKind.New,
            3 when identifier.SequenceEqual("put") => MatchingKeywordKind.Put,
            4 when identifier.SequenceEqual("else") => MatchingKeywordKind.Else,
            4 when identifier.SequenceEqual("enum") => MatchingKeywordKind.Enum,
            5 when identifier.SequenceEqual("while") => MatchingKeywordKind.While,
            5 when identifier.SequenceEqual("class") => MatchingKeywordKind.Class,
            5 when identifier.SequenceEqual("union") => MatchingKeywordKind.Union,
            5 when identifier.SequenceEqual("const") => MatchingKeywordKind.Const,
            6 when identifier.SequenceEqual("return") => MatchingKeywordKind.Return,
            6 when identifier.SequenceEqual("public") => MatchingKeywordKind.Public,
            6 when identifier.SequenceEqual("extern") => MatchingKeywordKind.Extern,
            6 when identifier.SequenceEqual("sealed") => MatchingKeywordKind.Sealed,
            6 when identifier.SequenceEqual("struct") => MatchingKeywordKind.Struct,
            6 when identifier.SequenceEqual("static") => MatchingKeywordKind.Static,
            7 when identifier.SequenceEqual("private") => MatchingKeywordKind.Private,
            8 when identifier.SequenceEqual("internal") => MatchingKeywordKind.Internal,
            9 when identifier.SequenceEqual("protected") => MatchingKeywordKind.Protected,
            9 when identifier.SequenceEqual("namespace") => MatchingKeywordKind.Namespace,
            9 when identifier.SequenceEqual("interface") => MatchingKeywordKind.Interface,
            _ => MatchingKeywordKind.None,
        };
    }

    /// <summary> Returns the corresponding enum for the given operator character. Returns NullToken if no operator matches. </summary>
    /// <param name="ch"> The character to check against. </param>
    private static (bool, TokenKind) IsOperator(char ch) => ch switch
    {
        '!' => (true, TokenKind.ExclamationMark),
        '"' => (true, TokenKind.DoubleQuote),
        '#' => (true, TokenKind.Octothorpe),
        '%' => (true, TokenKind.Percentage),
        '&' => (true, TokenKind.Ampersand),
        '\'' => (true, TokenKind.SingleQuote),
        '(' => (true, TokenKind.LeftParen),
        ')' => (true, TokenKind.RightParen),
        '*' => (true, TokenKind.Asterisk),
        '+' => (true, TokenKind.Plus),
        ',' => (true, TokenKind.Comma),
        '-' => (true, TokenKind.Minus),
        '.' => (true, TokenKind.Dot),
        '/' => (true, TokenKind.ForwardSlash),
        ':' => (true, TokenKind.Colon),
        ';' => (true, TokenKind.Semicolon),
        '<' => (true, TokenKind.LessThanSign),
        '=' => (true, TokenKind.Equals),
        '>' => (true, TokenKind.GreaterThanSign),
        '?' => (true, TokenKind.QuestionMark),
        '@' => (true, TokenKind.AtSymbol),
        '[' => (true, TokenKind.LeftBracket),
        '\\' => (true, TokenKind.BackwardSlash),
        ']' => (true, TokenKind.RightBracket),
        '^' => (true, TokenKind.Caret),
        '`' => (true, TokenKind.Backtick),
        '{' => (true, TokenKind.LeftBrace),
        '}' => (true, TokenKind.RightBrace),
        '~' => (true, TokenKind.Tilde),
        _ => (false, TokenKind.NullToken)
    };

    /// <summary> Peek ahead in the program string by specified offset. </summary>
    /// <param name="offset"> Offset by which to peek ahead. By default, it is 1. </param>
    /// <returns> char at the index peeked. Returns '\0' if the offset added to current index exceeds the program string length. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char Peek(int offset = 1) => current + offset < text.Length ? text[current + offset] : '\0';
}
