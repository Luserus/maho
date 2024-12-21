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
        TokenKind kind, next, last;
        
        kind = CurrentToken.Kind;
        next = Peek().Kind; 
        last = Peek(2).Kind;

        // Pair of 3 operators:
        if (kind is TokenKind.LessThanSign && next is TokenKind.LessThanSign && last is TokenKind.LessThanSign)
            return ("<<<", TokenKind.LessThanLessThanLessThanSigns);

        // Pair os 2 operators:
        else if (kind is TokenKind.Equals && next is TokenKind.Equals)
            return ("==", TokenKind.EqualsEquals);
        else if (kind is TokenKind.ExclamationMark && next is TokenKind.Equals)
            return ("!=", TokenKind.ExclamationEquals);
        else if (kind is TokenKind.LessThanSign && next is TokenKind.LessThanSign)
            return ("<<", TokenKind.LessThanLessThanSigns);
        else if (kind is TokenKind.GreaterThanSign && next is TokenKind.GreaterThanSign)
            return (">>", TokenKind.GreaterThanGreaterThanSigns);
        else if (kind is TokenKind.LessThanSign && next is TokenKind.Equals)
            return ("<=", TokenKind.LessThanEquals);
        else if (kind is TokenKind.GreaterThanSign && next is TokenKind.Equals)
            return (">=", TokenKind.GreaterThanEquals);
        else if (kind is TokenKind.Ampersand && next is TokenKind.Ampersand)
            return ("&&", TokenKind.AmpersandAmpersand);
        else if (kind is TokenKind.VerticalBar && next is TokenKind.VerticalBar)
            return ("||", TokenKind.VerticalBarVerticalBar);

        // Single operator:
        else if (kind is TokenKind.Plus)
            return ("+", TokenKind.Plus);
        else if (kind is TokenKind.Minus)
            return ("-", TokenKind.Minus);
        else if (kind is TokenKind.Asterisk)
            return ("*", TokenKind.Asterisk);
        else if (kind is TokenKind.ForwardSlash)
            return ("/", TokenKind.ForwardSlash);
        else if (kind is TokenKind.Percentage)
            return ("%", TokenKind.Percentage);
        else if (kind is TokenKind.Ampersand)
            return ("&", TokenKind.Ampersand);
        else if (kind is TokenKind.VerticalBar)
            return ("|", TokenKind.VerticalBar);
        else if (kind is TokenKind.LessThanSign)
            return ("<", TokenKind.LessThanSign);
        else if (kind is TokenKind.GreaterThanSign)
            return (">", TokenKind.GreaterThanSign);
        else if (kind is TokenKind.QuestionMark)
            return ("?", TokenKind.QuestionMark);

        // No operator matched
        return ("\0", TokenKind.NullToken);
    }


    /// <summary> Peek ahead in the tokens list by specified offset. </summary>
    /// <param name="offset"> Offset by which to peek ahead. By default, it is 1. </param>
    /// <returns> Token at the index peeked. Returns last token from the list if the offset added to current index exceeds the token list count. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Token Peek(int offset = 1)
    {
        if (current + offset < tokens.Count)
            return tokens[current + offset];
        else
            return tokens[^1];
    }

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
        return new(CurrentToken.Value, CurrentToken.CharNumber, CurrentToken.LineNumber, CurrentToken.ColumnNumber, TokenKind.MissingToken);
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
        return new(CurrentToken.Value, CurrentToken.CharNumber, CurrentToken.LineNumber, CurrentToken.ColumnNumber, TokenKind.MissingToken);

    }

    /// <summary> Ignores all whitespace and returns the next non-whitespace token without advancing the current index. </summary>
    /// <returns> Next non-whitespace token. </returns>
    private Token IgnoreSpace()
    {
        var current = this.current;

        while (WhitespacePeek(current).Kind is TokenKind.Whitespace || WhitespacePeek(current).Kind is TokenKind.Tabspace || WhitespacePeek(current).Kind is TokenKind.Newline)
            current++;

        return WhitespacePeek(current);

        Token WhitespacePeek(int current, int offset = 1) => tokens[current + offset];
    }

    /// <summary> Adds all the whitespaces until next non-whitespace token to a whitespace List and advances the current index. </summary>
    private void RemoveSpace()
    {
        while (CurrentToken.Kind is TokenKind.Whitespace || CurrentToken.Kind is TokenKind.Tabspace || CurrentToken.Kind is TokenKind.Newline)
            WhitespaceTokens.Add(Consume());
    }
}