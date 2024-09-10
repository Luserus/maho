using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Maho.Syntax
{
    /// <summary> Lexes the program string into tokens which is later passed to the Parser for syntactic analysis. </summary>
    /// <param name="program"> The program string to lex. </param>
    /// <param name="tabwidth"> The tab width used by the Lexer to increment column number. By default, it is assumed to be 4. </param>
    internal sealed class Lexer(string program, int tabwidth = 4)
    {
        /// <value> The program string. </value>
        /// <remarks> This field is readonly. </remarks>
        public string Program { get; } = program;
        /// <value> The tab width used by the Lexer to increment column number. By default, it is assumed to be 4. </value>
        /// <remarks> This field is readonly. </remarks>
        public int Tabwidth { get; } = tabwidth;
        /// <value> Tokens lexed by the Lexer in the form of an Immutable array. </value>
        /// <remarks> This field is readonly. </remarks>
        public ImmutableArray<Token> Tokens { get => [.. tokens]; }

        /// <summary> Tokens lexed by the Lexer. </summary>
        private readonly List<Token> tokens = [];
        /// <summary> Current index of char being read in the program string. </summary>
        private int current = 0;
        /// <summary> Current Line number in the program. </summary>
        private int lineNumber = 0;
        /// <summary> Current Column number in the program. </summary>
        private int columnNumber = 0;

        /// <summary> Lexes the program string into individual tokens. </summary>
        public void Lex()
        {
            char ch = Program[current];
            Token token = new();

            while (ch != '\0')
            {
                if (ch == ' ') // If ch is whitespace.
                {
                    EndToken(ref token);
                    token.Data.Append(ch);
                    token.Kind = TokenKind.Whitespace;
                    Consume();
                    EndToken(ref token);
                }
                else if (ch == '\t') // If ch is a tabspace.
                {
                    EndToken(ref token);
                    token.Data.Append(ch);
                    token.Kind = TokenKind.Tabspace;
                    Consume(columnIncr: Tabwidth);
                    EndToken(ref token);
                }
                else if (ch == '\n' || ch == '\r') // If ch is newline or carriage return.
                {
                    EndToken(ref token);
                    token.Data.Append(ch);
                    token.Kind = TokenKind.Newline;
                    Consume();
                    lineNumber += 1;
                    columnNumber = 0;
                    EndToken(ref token);
                }
                else if (char.IsLetter(ch)) // If ch is any unicode character.
                {
                    token.Data.Append(ch);

                    if (token.Kind is TokenKind.NullToken)
                        token.Kind = TokenKind.Identifier;

                    Consume();
                }
            }
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
        /// <param name="lineIncr"> Value by which Line number is to be incremented. By default it is 0. </param>
        /// <param name="columnIncr"> Value by which Column number is to be incremented. By default it is 1. </param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Consume(int currentIncr = 1, int columnIncr = 1)
        {
            current += currentIncr;
            columnNumber += columnIncr;
        }

        /// <summary> End the current Token and add it to the Tokens list. </summary>
        /// <param name="token"> The Token to be added to the list. </param>
        private void EndToken(ref Token token)
        {
            if (token.Kind is TokenKind.NullToken)
                return;

            token.LineNumber = lineNumber;
            token.ColumnNumber = columnNumber;

            tokens.Add(token);

            token.Data.Clear();
            token.Kind = TokenKind.NullToken;
        }
    }
}