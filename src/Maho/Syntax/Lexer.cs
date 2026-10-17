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

            if (current >= text.Length)
            {
                Tokens.Add(new(text, new TextSpan(text.Length, 0), TokenKind.EndToken, leadingTrivia, []));
                return Tokens;
            }

            bool isEscapedIdentifier = CurrentChar == '`' && UnicodeIdentifier.IsIdentifierStart(text, current + 1, out _);
            var (span, kind) = LexTokenData();
            var trailingTrivia = LexTrivia();
            var matching = MatchingKeywordKind.None;

            if (kind is TokenKind.Identifier && !isEscapedIdentifier)
                matching = MatchKeywordKind(span);

            Tokens.Add(new Token(text, span, kind, leadingTrivia, trailingTrivia, matching));
        }

        // Add an EndToken at the end of the list to tell the parser when the final token has been reached.
        Tokens.Add(new Token(text, new TextSpan(text.Length, 0), TokenKind.EndToken, [], []));

        return Tokens;
    }

    /// <summary> Current token kind. </summary>
    private TokenKind kind = TokenKind.NullToken;

    /// <summary> Lexes a part of the program and returns the required token data. </summary>
    /// <returns> The token data for creating a token. </returns>
    private (TextSpan Span, TokenKind kind) LexTokenData()
    {
        var start = current;

        if (CurrentChar == '`' && UnicodeIdentifier.IsIdentifierStart(text, current + 1, out _))
        {
            current++; // skip opening '`'
            var idStart = current;

            while (UnicodeIdentifier.IsIdentifierContinue(text, current, out int advance))
                current += advance;

            if (CurrentChar == '`')
            {
                var idSpan = new TextSpan(idStart, current - idStart);
                current++; // skip closing '`'
                kind = TokenKind.Identifier;

                return (idSpan, kind);
            }
            else
            {
                ReportUnterminatedLiteral(start, TokenKind.Identifier);
                kind = TokenKind.Identifier;

                return (new TextSpan(idStart, current - idStart), kind);
            }
        }
        else if (UnicodeIdentifier.IsIdentifierStart(text, current, out int startLen))
        {
            kind = TokenKind.Identifier;
            current += startLen;

            while (UnicodeIdentifier.IsIdentifierContinue(text, current, out int continueLen))
                current += continueLen;
        }
        else if (char.IsAsciiDigit(CurrentChar))
        {
            if (CurrentChar == '0' && (Peek(1) is 'b' or 'B') && (Peek(2) is '0' or '1' || (Peek(2) == '_' && Peek(3) is '0' or '1')))
            {
                kind = TokenKind.Integer;
                current += 2; // skip '0b' or '0B'

                while (CurrentChar is '0' or '1' or '_')
                    current++;

                if (ScanNumericSuffix())
                    kind = TokenKind.SuffixedInteger;
            }
            else if (CurrentChar == '0' && (Peek(1) is 'x' or 'X') && (char.IsAsciiHexDigit(Peek(2)) || (Peek(2) == '_' && char.IsAsciiHexDigit(Peek(3)))))
            {
                kind = TokenKind.Integer;
                current += 2; // skip '0x' or '0X'

                while (char.IsAsciiHexDigit(CurrentChar) || CurrentChar == '_')
                    current++;

                if (ScanNumericSuffix())
                    kind = TokenKind.SuffixedInteger;
            }
            else
            {
                kind = TokenKind.Integer;

                while (char.IsAsciiDigit(CurrentChar) || CurrentChar == '_')
                    current++;

                if (CurrentChar == '.' && char.IsAsciiDigit(Peek(1)))
                {
                    kind = TokenKind.Float;
                    current++; // skip '.'

                    while (char.IsAsciiDigit(CurrentChar) || CurrentChar == '_')
                        current++;
                }

                if (IsExponentStart())
                {
                    kind = TokenKind.Float;
                    current++; // skip 'e' or 'E'

                    if (CurrentChar is '+' or '-')
                        current++;

                    while (char.IsAsciiDigit(CurrentChar) || CurrentChar == '_')
                        current++;
                }

                if (ScanNumericSuffix())
                    kind = kind == TokenKind.Float ? TokenKind.SuffixedFloat : TokenKind.SuffixedInteger;
            }

            var span = new TextSpan(start, current - start);
            return (span, kind);
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
                current++; // skip '.'

                while (char.IsAsciiDigit(CurrentChar) || CurrentChar == '_')
                    current++;

                if (IsExponentStart())
                {
                    kind = TokenKind.Float;
                    current++; // skip 'e' or 'E'

                    if (CurrentChar is '+' or '-')
                        current++;

                    while (char.IsAsciiDigit(CurrentChar) || CurrentChar == '_')
                        current++;
                }

                if (ScanNumericSuffix())
                    kind = TokenKind.SuffixedFloat;

                var span = new TextSpan(start, current - start);
                return (span, kind);
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

        TextSpan nonLiteralSpan = new(start, current - start);
        return (nonLiteralSpan, kind);
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

                if (UnicodeIdentifier.IsIdentifierStart(text, current, out int suffixStartLen))
                {
                    current += suffixStartLen;
                    while (UnicodeIdentifier.IsIdentifierContinue(text, current, out int continueLen))
                        current += continueLen;

                    kind = tokenKind == TokenKind.Char ? TokenKind.SuffixedChar : TokenKind.SuffixedString;
                    return (new TextSpan(start, current - start), kind);
                }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsExponentStart()
    {
        if (CurrentChar is not ('e' or 'E'))
            return false;

        char p1 = Peek(1);
        if (char.IsAsciiDigit(p1) || (p1 == '_' && char.IsAsciiDigit(Peek(2))))
            return true;

        if (p1 is '+' or '-')
        {
            char p2 = Peek(2);
            return char.IsAsciiDigit(p2) || (p2 == '_' && char.IsAsciiDigit(Peek(3)));
        }

        return false;
    }

    private bool ScanNumericSuffix()
    {
        if (UnicodeIdentifier.IsIdentifierStart(text, current, out int startLen))
        {
            if (CurrentChar == '_' && !UnicodeIdentifier.IsIdentifierContinue(text, current + 1, out _))
                return false;

            current += startLen;
            while (UnicodeIdentifier.IsIdentifierContinue(text, current, out int continueLen))
                current += continueLen;

            return true;
        }

        return false;
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
            else if (CurrentChar == '\r' && Peek() == '\n') // CRLF line endings
            {
                current += 2;
                kind = SyntaxTriviaKind.EndOfLine;
                tokenKind = TokenKind.Newline;

                trivias.Add(new(kind, new TextSpan(start, current - start)));
            }
            else if (CurrentChar == '/' && Peek() == '/')
            {
                current += 2;
                kind = SyntaxTriviaKind.SingleLineComment;
                tokenKind = TokenKind.SingleLineComment;

                while (CurrentChar != '\0' && CurrentChar is not '\r' and not '\n')
                    current++;

                trivias.Add(new(kind, new TextSpan(start, current - start)));
            }
            else if (CurrentChar == '/' && Peek() == '*')
            {
                current += 2;
                kind = SyntaxTriviaKind.MultiLineComment;
                tokenKind = TokenKind.MultiLineComment;

                while (true)
                {
                    if (CurrentChar == '\0')
                    {
                        ReportUnterminatedMultiLineComment(start);
                        break;
                    }

                    if (CurrentChar == '*' && Peek() == '/')
                    {
                        current += 2;
                        break;
                    }

                    current++;
                }

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
            2 when identifier.SequenceEqual("as") => MatchingKeywordKind.As,
            3 when identifier.SequenceEqual("for") => MatchingKeywordKind.For,
            3 when identifier.SequenceEqual("get") => MatchingKeywordKind.Get,
            3 when identifier.SequenceEqual("new") => MatchingKeywordKind.New,
            3 when identifier.SequenceEqual("put") => MatchingKeywordKind.Put,
            3 when identifier.SequenceEqual("set") => MatchingKeywordKind.Set,
            3 when identifier.SequenceEqual("var") => MatchingKeywordKind.Var,
            3 when identifier.SequenceEqual("dyn") => MatchingKeywordKind.Dyn,
            3 when identifier.SequenceEqual("int") => MatchingKeywordKind.Int,
            4 when identifier.SequenceEqual("goto") => MatchingKeywordKind.Goto,
            4 when identifier.SequenceEqual("else") => MatchingKeywordKind.Else,
            4 when identifier.SequenceEqual("enum") => MatchingKeywordKind.Enum,
            4 when identifier.SequenceEqual("with") => MatchingKeywordKind.With,
            4 when identifier.SequenceEqual("expr") => MatchingKeywordKind.Expr,
            4 when identifier.SequenceEqual("type") => MatchingKeywordKind.Type,
            4 when identifier.SequenceEqual("stmt") => MatchingKeywordKind.Stmt,
            5 when identifier.SequenceEqual("ident") => MatchingKeywordKind.Ident,
            5 when identifier.SequenceEqual("token") => MatchingKeywordKind.Token,
            5 when identifier.SequenceEqual("while") => MatchingKeywordKind.While,
            5 when identifier.SequenceEqual("using") => MatchingKeywordKind.Using,
            5 when identifier.SequenceEqual("float") => MatchingKeywordKind.Float,
            5 when identifier.SequenceEqual("class") => MatchingKeywordKind.Class,
            5 when identifier.SequenceEqual("union") => MatchingKeywordKind.Union,
            5 when identifier.SequenceEqual("where") => MatchingKeywordKind.Where,
            5 when identifier.SequenceEqual("const") => MatchingKeywordKind.Const,
            5 when identifier.SequenceEqual("macro") => MatchingKeywordKind.Macro,
            6 when identifier.SequenceEqual("nameof") => MatchingKeywordKind.Nameof,
            6 when identifier.SequenceEqual("tokens") => MatchingKeywordKind.Tokens,
            6 when identifier.SequenceEqual("global") => MatchingKeywordKind.Global,
            6 when identifier.SequenceEqual("return") => MatchingKeywordKind.Return,
            6 when identifier.SequenceEqual("public") => MatchingKeywordKind.Public,
            6 when identifier.SequenceEqual("extern") => MatchingKeywordKind.Extern,
            6 when identifier.SequenceEqual("struct") => MatchingKeywordKind.Struct,
            6 when identifier.SequenceEqual("static") => MatchingKeywordKind.Static,
            6 when identifier.SequenceEqual("unsafe") => MatchingKeywordKind.Unsafe,
            6 when identifier.SequenceEqual("prefix") => MatchingKeywordKind.Prefix,
            7 when identifier.SequenceEqual("partial") => MatchingKeywordKind.Partial,
            7 when identifier.SequenceEqual("private") => MatchingKeywordKind.Private,
            7 when identifier.SequenceEqual("virtual") => MatchingKeywordKind.Virtual,
            7 when identifier.SequenceEqual("postfix") => MatchingKeywordKind.Postfix,
            8 when identifier.SequenceEqual("Unsealed") => MatchingKeywordKind.Unsealed,
            8 when identifier.SequenceEqual("internal") => MatchingKeywordKind.Internal,
            8 when identifier.SequenceEqual("operator") => MatchingKeywordKind.Operator,
            9 when identifier.SequenceEqual("intrinsic") => MatchingKeywordKind.Intrinsic,
            9 when identifier.SequenceEqual("attribute") => MatchingKeywordKind.Attribute,
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
        '|' => (true, TokenKind.VerticalBar),
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
        '$' => (true, TokenKind.Dollar),
        _ => (false, TokenKind.NullToken)
    };

    /// <summary> Peek ahead in the program string by specified offset. </summary>
    /// <param name="offset"> Offset by which to peek ahead. By default, it is 1. </param>
    /// <returns> char at the index peeked. Returns '\0' if the offset added to current index exceeds the program string length. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char Peek(int offset = 1) => current + offset < text.Length ? text[current + offset] : '\0';
}