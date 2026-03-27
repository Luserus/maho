using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Maho.Diagnostics;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Lexes the program string into tokens which is later passed to the Parser for syntactic analysis. </summary>
internal sealed class Lexer
{
    private readonly DiagnosticsManager diagnostics;
    /// <summary> Current index of char being read from the program string. </summary>
    private int current;
    /// <summary> The source text of the program. </summary>
    private readonly SourceText text;

    private char CurrentChar => current >= text.Length ? '\0' : text[current];

    /// <summary> Tokens lexed by the Lexer. </summary>
    public List<Token> Tokens { get; } = new(256);

    /// <summary> Initializes a new instance of the Lexer class. </summary>
    /// <param name="sourceText"> Source text of the program. </param>
    public Lexer(SourceText sourceText, DiagnosticsManager diagnosticsManager)
    {
        text = sourceText;
        diagnostics = diagnosticsManager;
    }

    /// <summary> Lexes the program string into tokens with trivia. </summary>
    public void Lex()
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
            current++;

            if (opKind is TokenKind.SingleQuote)
            {
                kind = TokenKind.Char;

                while (CurrentChar != '\'')
                    current++;
                
                current++;
            }
            else if (opKind is TokenKind.DoubleQuote)
            {
                kind = TokenKind.String;

                while (CurrentChar != '"')
                    current++;

                current++;
            }
            else if (opKind is TokenKind.Dot && kind is not TokenKind.Identifier and not TokenKind.String and not TokenKind.Char and not TokenKind.Float)
            {
                kind = TokenKind.Float;
                current++;

                while (char.IsAsciiDigit(Peek(0)))
                    current++;
            }
            else
                kind = opKind;
        }
        else
        {
            kind = TokenKind.BadToken;
            current++;
        }

        TextSpan span = new(start, current - start);

        return (span, kind);
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

    private MatchingKeywordKind MatchKeywordKind(TextSpan span)
    {
        return text.ToString(span) switch
        {
            "if" => MatchingKeywordKind.If,
            "else" => MatchingKeywordKind.Else,
            "while" => MatchingKeywordKind.While,
            "return" => MatchingKeywordKind.Return,
            "public" => MatchingKeywordKind.Public,
            "private" => MatchingKeywordKind.Private,
            "internal" => MatchingKeywordKind.Internal,
            "extern" => MatchingKeywordKind.Extern,
            "protected" => MatchingKeywordKind.Protected,
            "sealed" => MatchingKeywordKind.Sealed,
            "namespace" => MatchingKeywordKind.Namespace,
            "struct" => MatchingKeywordKind.Struct,
            "class" => MatchingKeywordKind.Class,
            "enum" => MatchingKeywordKind.Enum,
            "union" => MatchingKeywordKind.Union,
            "interface" => MatchingKeywordKind.Interface,
            "static" => MatchingKeywordKind.Static,
            "for" => MatchingKeywordKind.For,
            "new" => MatchingKeywordKind.New,
            "put" => MatchingKeywordKind.Put,
            "cons" => MatchingKeywordKind.Const,
            _ => MatchingKeywordKind.None,
        };
    }

    /// <summary> Returns the corresponding enum for the given operator character. Returns NullToken if no operator matches. </summary>
    /// <param name="ch"> The character to check against. </param>
    private static (bool, TokenKind) IsOperator(char ch)
    {
        return ch switch
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
    }

    /// <summary> Peek ahead in the program string by specified offset. </summary>
    /// <param name="offset"> Offset by which to peek ahead. By default, it is 1. </param>
    /// <returns> char at the index peeked. Returns '\0' if the offset added to current index exceeds the program string length. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char Peek(int offset = 1) => current + offset < text.Length ? text[current + offset] : '\0';

    /// <summary> Gets all the tokens as string in json form. </summary>
    /// <returns> Tokens in string form. </returns>
    public override string ToString()
    {
        StringBuilder sb = new();

        sb.AppendLine("Lexed Tokens:\n");

        foreach (var token in Tokens)
        {
            sb.AppendLine("Token");
            sb.AppendLine("{");
            sb.AppendLine($"    Value: \"{token.Value}\",");
            sb.AppendLine($"    Kind: {token.Kind}\n");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}