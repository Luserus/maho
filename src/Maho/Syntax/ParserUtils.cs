using System;
using System.Runtime.CompilerServices;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    /// <summary> Returns the unary operator precedence. </summary>
    /// <returns> The unary operator precedence. </returns>
    private int GetUnaryOperatorPrecedence()
    {
        var (_, Kind) = GetCombinedTokenData();

        return Kind switch
        {
            TokenKind.Plus or TokenKind.Minus or TokenKind.ExclamationMark => 6,
            _ => 0
        };
    }

    /// <summary> Returns the binary operator precedence. </summary>
    /// <returns> The binary operator precedence. </returns>
    private int GetBinaryOperatorPrecedence()
    {
        var (_, Kind) = GetCombinedTokenData();

        return Kind switch
        {
            TokenKind.Asterisk or TokenKind.ForwardSlash => 5,
            TokenKind.Plus or TokenKind.Minus => 4,
            TokenKind.EqualsEquals or TokenKind.ExclamationEquals => 3,
            TokenKind.AmpersandAmpersand => 2,
            TokenKind.VerticalBarVerticalBar => 1,
            _ => 0
        };
    }

    /// <summary> Returns the value and combined form of combined operator token types. </summary>
    /// <returns> The string value and TokenKind of the combined operators. </returns>
    private (string Value, TokenKind Kind) GetCombinedTokenData()
    {
        Token nextToken, lastToken;

        nextToken = Peek();
        lastToken = Peek(2);

        TokenKind kind, next, last;
        
        kind = CurrentToken.Kind;
        next = nextToken.Kind; 
        last = lastToken.Kind;

        // Pair of 3 operators
        if (kind is TokenKind.LessThanSign && CurrentToken.TrailingTrivia.Length == 0 && next is TokenKind.LessThanSign && nextToken.TrailingTrivia.Length == 0 && last is TokenKind.LessThanSign)
            return ("<<<", TokenKind.LessThanLessThanLessThanSigns);

        // Pair of 2 operators
        else if (kind is TokenKind.Equals && CurrentToken.TrailingTrivia.Length == 0 && next is TokenKind.Equals)
            return ("==", TokenKind.EqualsEquals);
        else if (kind is TokenKind.ExclamationMark && CurrentToken.TrailingTrivia.Length == 0 && next is TokenKind.Equals)
            return ("!=", TokenKind.ExclamationEquals);
        else if (kind is TokenKind.LessThanSign && CurrentToken.TrailingTrivia.Length == 0 && next is TokenKind.LessThanSign)
            return ("<<", TokenKind.LessThanLessThanSigns);
        else if (kind is TokenKind.GreaterThanSign && CurrentToken.TrailingTrivia.Length == 0 && next is TokenKind.GreaterThanSign)
            return (">>", TokenKind.GreaterThanGreaterThanSigns);
        else if (kind is TokenKind.LessThanSign && CurrentToken.TrailingTrivia.Length == 0 && next is TokenKind.Equals)
            return ("<=", TokenKind.LessThanEquals);
        else if (kind is TokenKind.GreaterThanSign && CurrentToken.TrailingTrivia.Length == 0 && next is TokenKind.Equals)
            return (">=", TokenKind.GreaterThanEquals);
        else if (kind is TokenKind.Ampersand && CurrentToken.TrailingTrivia.Length == 0 && next is TokenKind.Ampersand)
            return ("&&", TokenKind.AmpersandAmpersand);
        else if (kind is TokenKind.VerticalBar && CurrentToken.TrailingTrivia.Length == 0 && next is TokenKind.VerticalBar)
            return ("||", TokenKind.VerticalBarVerticalBar);

        // Single operator
        else if (kind is TokenKind.Plus)
            return ("+", kind);
        else if (kind is TokenKind.Minus)
            return ("-", kind);
        else if (kind is TokenKind.Asterisk)
            return ("*", kind);
        else if (kind is TokenKind.ForwardSlash)
            return ("/", kind);
        else if (kind is TokenKind.Percentage)
            return ("%", kind);
        else if (kind is TokenKind.Ampersand)
            return ("&", kind);
        else if (kind is TokenKind.VerticalBar)
            return ("|", kind);
        else if (kind is TokenKind.LessThanSign)
            return ("<", kind);
        else if (kind is TokenKind.GreaterThanSign)
            return (">", kind);
        else if (kind is TokenKind.QuestionMark)
            return ("?", kind);

        // No operator matched
        return ("\0", TokenKind.NullToken);
    }


    /// <summary> Peek ahead in the tokens list by specified offset. </summary>
    /// <param name="offset"> Offset by which to peek ahead. By default, it is 1. </param>
    /// <returns> Token at the index peeked. Returns last token from the list if the offset added to current index exceeds the token list count. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token Peek(int offset = 1) => current + offset < tokens.Count ? tokens[current + offset] : tokens[^1];

    /// <summary> Consumes the current token and moves the current index ahead by 1. </summary>
    /// <returns> The token consumed. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token Consume()
    {
        var currentToken = CurrentToken;
        current++;
        return currentToken;
    }

    /// <summary> Matches the given TokenKind with current token's TokenKind. </summary>
    /// <param name="other"> The TokenKind to compare current token's TokenKind against. </param>
    /// <returns> Current token if match is successful, otherwise a missing token. </returns>
    private Token Match(TokenKind other)
    {
        if (CurrentToken.Kind == other)
            return Consume();

        Consume();
        return new(CurrentToken.Value, CurrentToken.Span, TokenKind.MissingToken, CurrentToken.LeadingTrivia, CurrentToken.TrailingTrivia);
    }

    /// <summary> Matches all the given TokenKinds with current token's TokenKind. </summary>
    /// <param name="others"> The TokenKinds to compare current token's TokenKind against. </param>
    /// <returns> Current token if any match is successful, otherwise a missing token. </returns>
    private Token Match(params TokenKind[] others)
    {
        var matched = false;

        foreach (var kind in others)
            if (CurrentToken.Kind == kind)
            {
                matched = true;
                break;
            }

        if (matched)
            return Consume();

        Consume();
        return new(CurrentToken.Value, CurrentToken.Span, TokenKind.MissingToken, CurrentToken.LeadingTrivia, CurrentToken.TrailingTrivia);
    }
}