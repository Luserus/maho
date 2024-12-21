using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Maho.Syntax;

/// <summary> Lexes the program string into tokens which is later passed to the Parser for syntactic analysis. </summary>
internal sealed class Lexer()
{
    /// <summary> The program string. </summary>
    /// <remarks> This field is read only. </remarks>
    public string Program { get; private set; } = null!;
    /// <summary> Tokens lexed by the Lexer. </summary>
    public List<Token> Tokens { get; } = new(256);

    /// <summary> Current index of char being read from the program string. </summary>
    private int current;
    /// <summary> Current Line number in the program. </summary>
    private int lineNumber;
    /// <summary> Current Column number in the program. </summary>
    private int columnNumber;

    /// <summary> Lexes the program string into individual tokens. </summary>
    /// <param name="program"> The program string to lex. </param>
    public void Lex(string program)
    {
        Program = program;
        current = default;
        lineNumber = 1;
        columnNumber = 1;

        Token token = new();

        while (current < Program.Length)
        {
            char ch = Program[current];

            if (ch == ' ') // If ch is whitespace.
            {
                EndToken(ref token);
                token.Data.Append(ch);
                token.Kind = TokenKind.Whitespace;
                SetTokenData(ref token);
                Consume();
                EndToken(ref token);
            }
            else if (ch == '\t') // If ch is a tabspace.
            {
                EndToken(ref token);
                token.Data.Append(ch);
                token.Kind = TokenKind.Tabspace;
                SetTokenData(ref token);
                Consume();
                EndToken(ref token);
            }
            else if (ch == '\n' || ch == '\r') // If ch is newline or carriage return.
            {
                EndToken(ref token);
                token.Data.Append(ch);
                token.Kind = TokenKind.Newline;
                SetTokenData(ref token);
                ++current;
                lineNumber += 1;
                columnNumber = 0;
                EndToken(ref token);
            }
            else if (char.IsLetter(ch)) // If ch is any unicode character.
            {
                token.Data.Append(ch);

                if (token.Kind is TokenKind.NullToken)
                {
                    token.Kind = TokenKind.Identifier;
                    SetTokenData(ref token);
                }

                Consume();
            }
            else if (char.IsAsciiDigit(ch)) // If ch is any ASCII Digit [0 - 9].
            {
                token.Data.Append(ch);

                if (token.Kind is TokenKind.NullToken)
                {
                    token.Kind = TokenKind.Integer;
                    SetTokenData(ref token);
                }

                Consume();
            }
            else if (IsOperator(ch) is (true, var kind)) // If ch is any operator symbol.
            {
                if (token.Kind is TokenKind.String)
                {
                    token.Data.Append(ch);
                    Consume();
                }
                else if (kind is TokenKind.Dot)
                {
                    if (token.Kind is TokenKind.Integer && char.IsAsciiDigit(Peek())) // If dot (.) comes before and after another integer. e.g: 69.420
                    {
                        token.Data.Append(ch);
                        token.Kind = TokenKind.Float;
                        Consume();
                    }
                    else if (token.Kind is TokenKind.NullToken && char.IsAsciiDigit(Peek())) // If dot (.) comes before an integer but after a NullToken. e.g: .420
                    {
                        token.Data.Append(ch);
                        token.Kind = TokenKind.Float;
                        Consume();
                    }
                    else // It is just a dot (.) operator.
                    {
                        EndToken(ref token);
                        token.Data.Append(ch);
                        token.Kind = kind;
                        SetTokenData(ref token);
                        Consume();
                        EndToken(ref token);
                    }
                }
                else // ch is any other operator symbol.
                {
                    EndToken(ref token);
                    token.Data.Append(ch);
                    token.Kind = kind;
                    SetTokenData(ref token);
                    Consume();
                    EndToken(ref token);
                }
            }
            else // If none of the cases satisy then the character is unsupported by the Lexer.
            {
                token.Data.Append(ch);
                token.Kind = TokenKind.BadToken;
                SetTokenData(ref token);
                Consume();
            }
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
            '_' => (true, TokenKind.Underscore),
            '`' => (true, TokenKind.Backtick),
            '{' => (true, TokenKind.LeftCurlyBrace),
            '}' => (true, TokenKind.RightCurlyBrace),
            '~' => (true, TokenKind.Tilde),
            _ => (false, TokenKind.NullToken)
        };
    }

    /// <summary> Sets the Token's Line number and Column number. </summary>
    /// <param name="token"> The Token to have its data set. </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetTokenData(ref Token token)
    {
        token.LineNumber = lineNumber;
        token.ColumnNumber = columnNumber;
        token.CharNumber = current + 1;
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

    /// <summary> Consume the current character. </summary>
    /// <param name="currentIncr"> Value by which current index is to be incremented. By default, it is 1. </param>
    /// <param name="columnNumIncr"> Value by which column number is to be incremented. By default it is 1. </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Consume(int currentIncr = 1, int columnNumIncr = 1)
    {
        current += currentIncr;
        columnNumber += columnNumIncr;
    }

    /// <summary> End the current Token and add it to the Tokens list. </summary>
    /// <param name="token"> The Token to be added to the list. </param>
    private void EndToken(ref Token token)
    {
        if (token.Kind is TokenKind.NullToken)
            return;

        token.Value = token.Data.ToString();

        Tokens.Add(token);

        token.Data.Clear();
        token.Kind = TokenKind.NullToken;
    }
}
