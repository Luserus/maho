using System;
using System.Collections.Generic;
using System.Text;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Lexes the program string into tokens which is later passed to the Parser for syntactic analysis. </summary>
internal sealed partial class Lexer
{
    /// <summary> Current index of char being read from the program string. </summary>
    private int current;
    /// <summary> The source text of the program. </summary>
    private readonly SourceText text;

    /// <summary> The program string. </summary>
    public string Program { get; }
    /// <summary> Tokens lexed by the Lexer. </summary>
    public List<Token> Tokens { get; } = new(256);

    /// <summary> Current character in the Program that is being read. </summary>
    private char CurrentChar => Program[current];

    /// <summary> Initializes a new instance of the Lexer class. </summary>
    /// <param name="sourceText"> Source text of the program. </param>
    public Lexer(SourceText sourceText)
    {
        text = sourceText;
        Program = text.ToString();
    }

    /// <summary> Lexes the program string into tokens with trivia. </summary>
    public void Lex()
    {
        while (current < Program.Length)
        {
            var leadingTrivia = LexTrivia();
            var (value, span, kind) = LexTokenData();
            var trailingTrivia = LexTrivia();

            Tokens.Add(new(value, span, kind, leadingTrivia, trailingTrivia));
        }

        // Add an EndToken at the end of the list to tell the parser when the final token has been reached.
        Tokens.Add(new("\0", new TextSpan(Program.Length, 0), TokenKind.EndToken, [], []));
    }

    /// <summary> Current token kind. </summary>
    private TokenKind kind = TokenKind.NullToken;

    /// <summary> Lexes a part of the program and returns the required token data. </summary>
    /// <returns> The token data for creating a token. </returns>
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

    /// <summary> Lexes a part of the program and returns all leading/trailing trivias before/after a token. </summary>
    /// <returns> The trivias as an array. </returns>
    private SyntaxTrivia[] LexTrivia()
    {
        List<SyntaxTrivia> trivias = [];
        var tokenKind = kind;

        while (current < Program.Length)
        {
            SyntaxTriviaKind kind;
            var start = current;
            string trivia;

            if (CurrentChar == ' ')
            {
                current++;
                kind = SyntaxTriviaKind.Whitespace;
                tokenKind = TokenKind.Whitespace;

                while (CurrentChar == ' ')
                    current++;

                trivia = Program[start..current];
                trivias.Add(new(trivia, kind, new TextSpan(start, current - start)));
            }
            else if (CurrentChar == '\t')
            {
                current++;
                kind = SyntaxTriviaKind.Whitespace;
                tokenKind = TokenKind.Tabspace;

                while (CurrentChar == '\t')
                    current++;

                trivia = Program[start..current];
                trivias.Add(new(trivia, kind, new TextSpan(start, current - start)));
            }
            else if (CurrentChar == '\n')
            {
                current++;
                kind = SyntaxTriviaKind.EndOfLine;
                tokenKind = TokenKind.Newline;

                trivia = Program[start..current];
                trivias.Add(new(trivia, kind, new TextSpan(start, current - start)));
            }
            else
                break;
        }

        kind = tokenKind;
        return [.. trivias];
    }
}