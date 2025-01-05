using System.Runtime.CompilerServices;

namespace Maho.Syntax;

internal sealed partial class Lexer
{
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