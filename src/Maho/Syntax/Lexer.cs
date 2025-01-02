using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Lexes the program string into tokens which is later passed to the Parser for syntactic analysis. </summary>
internal sealed class Lexer
{
    /// <summary> The program string. </summary>
    /// <remarks> This field is read only. </remarks>
    public string Program { get; private set; } = null!;
    /// <summary> Tokens lexed by the Lexer. </summary>
    public List<Token> Tokens { get; } = new(256);

    /// <summary> Current index of char being read from the program string. </summary>
    private int current;
    private char CurrentChar => Peek(0);

    /// <summary> Lexes the program string into individual tokens. </summary>
    public void Lex(string program)
    {
        Program = program;
        current = default;
        Tokens.Clear();

        while (current < Program.Length)
        {
            var leadingTrivia = LexTrivia();
            var (value, span, kind) = LexTokenData();
            var trailingTrivia = LexTrivia();

            Tokens.Add(new(value, span, kind, leadingTrivia, trailingTrivia));
        }

        // Add an EndToken at the end of the list to tell the parser when the final token has been reached.
        Tokens.Add(new(string.Empty, new(Program.Length, 0), TokenKind.EndToken, [], []));
    }

    private TokenKind kind = TokenKind.NullToken;

    private (string Value, TextSpan Span, TokenKind kind) LexTokenData()
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
            kind = opKind;
            current++;
        }
        else
        {
            kind = TokenKind.BadToken;
            current++;
        }

        var value = Program[start..current];
        TextSpan span = new(start, current - start);

        return (value, span, kind);
    }

    private SyntaxTrivia[] LexTrivia()
    {
        List<SyntaxTrivia> list = [];
        var start = current;
        StringBuilder buffer = new();
        var kind = SyntaxTriviaKind.Whitespace;
        var tokenKind = TokenKind.NullToken;

        while (current < Program.Length)
        {
            if (CurrentChar == ' ')
            {
                if (tokenKind is not TokenKind.Whitespace && buffer.Length > 0)
                    AddTrivia();

                tokenKind = TokenKind.Whitespace;
                kind = SyntaxTriviaKind.Whitespace;
                current++;
                buffer.Append(CurrentChar);       
            }
            else if (CurrentChar == '\t')
            {
                if (tokenKind is not TokenKind.Tabspace && buffer.Length > 0)
                    AddTrivia();

                tokenKind = TokenKind.Tabspace;
                kind = SyntaxTriviaKind.Whitespace;
                current++;
                buffer.Append(CurrentChar);
            }
            else if (CurrentChar == '\r' && Peek() == '\n')
            {
                if (buffer.Length > 0)
                    AddTrivia();

                tokenKind = TokenKind.Newline;
                kind = SyntaxTriviaKind.EndOfLine;
                current += 2;
                buffer.Append(CurrentChar);
                buffer.Append('\n');
            }
            else if (CurrentChar == '\n')
            {
                if (buffer.Length > 0)
                    AddTrivia();

                tokenKind = TokenKind.Newline;
                kind = SyntaxTriviaKind.EndOfLine;
                current++;
                buffer.Append(CurrentChar);
            }
            else
            {
                if (buffer.Length > 0)
                    AddTrivia();

                break;
            }
        }

        tokenKind = TokenKind.NullToken;
        return [.. list];

        void AddTrivia()
        {
            list.Add(new(buffer.ToString(), kind, new(start, buffer.Length)));
            start = current;
            buffer.Clear();
        }
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
            '[' => (true, TokenKind.LeftSquareBracket),
            '\\' => (true, TokenKind.BackwardSlash),
            ']' => (true, TokenKind.RightSqureBracket),
            '^' => (true, TokenKind.Caret),
            '`' => (true, TokenKind.Backtick),
            '{' => (true, TokenKind.LeftCurlyBrace),
            '}' => (true, TokenKind.RightCurlyBrace),
            '~' => (true, TokenKind.Tilde),
            _ => (false, TokenKind.NullToken)
        };
    }

    /// <summary> Peek ahead in the program string by specified offset. </summary>
    /// <param name="offset"> Offset by which to peek ahead. By default, it is 1. </param>
    /// <returns> char at the index peeked. Returns '\0' if the offset added to current index exceeds the program string length. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char Peek(int offset = 1)
    {
        if (current + offset < Program.Length)
            return Program[current + offset];
        else
            return '\0';
    }
}