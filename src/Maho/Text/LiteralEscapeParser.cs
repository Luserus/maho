using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Maho.Syntax;

namespace Maho.Text;

/// <summary> Classifies escape validation errors discovered while parsing quoted literals. </summary>
internal enum LiteralEscapeErrorKind : byte
{
    InvalidEscape,
    InvalidUnicodeEscape
}

/// <summary> Records an invalid escape sequence together with its exact source span. </summary>
internal readonly record struct LiteralEscapeError(TextSpan Span, LiteralEscapeErrorKind Kind, string Detail);

/// <summary> Decoded payload, Unicode scalar count, escape spans, and escape errors for a quoted literal. </summary>
internal sealed record ResolvedLiteralInfo(
    string DecodedValue,
    int ScalarCount,
    IReadOnlyList<TextSpan> EscapeSpans,
    IReadOnlyList<LiteralEscapeError> Errors)
{
    /// <summary> Gets whether escape parsing encountered one or more invalid escape sequences. </summary>
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Shared escape decoder and validator for Maho string and character literals (unsuffixed and suffixed).
/// Validates escape syntax, rejects UTF-16 surrogates and out-of-range Unicode scalars, counts <see cref="Rune"/> scalars,
/// and preserves exact source <see cref="TextSpan"/> coordinates for syntax highlighting.
/// </summary>
internal static class LiteralEscapeParser
{
    /// <summary> Parses and validates escape sequences in a quoted literal <see cref="Token"/>. </summary>
    public static ResolvedLiteralInfo Parse(Token token)
    {
        if (token.Kind is not (TokenKind.String or TokenKind.SuffixedString or TokenKind.Char or TokenKind.SuffixedChar))
            return new ResolvedLiteralInfo(token.ValueWithoutSuffix, 0, Array.Empty<TextSpan>(), Array.Empty<LiteralEscapeError>());

        char quote = token.Kind is TokenKind.Char or TokenKind.SuffixedChar ? '\'' : '"';
        ReadOnlySpan<char> sourceText = token.Span.End <= token.Source.Length
            ? token.Source.AsSpan(token.Span)
            : [];

        bool hasExactSourceSpans = !sourceText.IsEmpty && sourceText.SequenceEqual(token.Value);
        ReadOnlySpan<char> rawWithoutSuffix = hasExactSourceSpans
            ? StripSuffix(sourceText, token.HasSuffix, quote)
            : StripSuffix(token.Value.AsSpan(), token.HasSuffix, quote);

        // Strip enclosing quotes (tolerating unterminated recovery tokens).
        int openOffset = rawWithoutSuffix.Length > 0 && rawWithoutSuffix[0] == quote ? 1 : 0;
        int closeLength = rawWithoutSuffix.Length > openOffset && rawWithoutSuffix[^1] == quote
            ? rawWithoutSuffix.Length - 1
            : rawWithoutSuffix.Length;

        ReadOnlySpan<char> content = rawWithoutSuffix[openOffset..closeLength];
        int contentStartOffset = hasExactSourceSpans ? token.Span.Start + openOffset : 0;

        return ParseContent(content, contentStartOffset, hasExactSourceSpans);
    }

    private static ReadOnlySpan<char> StripSuffix(ReadOnlySpan<char> raw, bool hasSuffix, char quote)
    {
        if (!hasSuffix)
            return raw;

        int lastQuote = raw.LastIndexOf(quote);
        return lastQuote > 0 ? raw[..(lastQuote + 1)] : raw;
    }

    private static ResolvedLiteralInfo ParseContent(ReadOnlySpan<char> content, int contentStartOffset, bool trackSourceSpans)
    {
        var sb = new StringBuilder(content.Length);
        var escapeSpans = new List<TextSpan>();
        List<LiteralEscapeError>? errors = null;
        int scalarCount = 0;

        int i = 0;
        while (i < content.Length)
        {
            if (content[i] != '\\')
            {
                if (Rune.DecodeFromUtf16(content[i..], out var rune, out int consumed) == OperationStatus.Done)
                {
                    sb.Append(content.Slice(i, consumed));
                }
                else
                {
                    sb.Append(content.Slice(i, Math.Max(1, consumed)));
                }

                scalarCount++;
                i += Math.Max(1, consumed);
                continue;
            }

            int escStart = i;
            if (i + 1 >= content.Length)
            {
                var errSpan = trackSourceSpans ? new TextSpan(contentStartOffset + escStart, 1) : default;
                (errors ??= []).Add(new LiteralEscapeError(errSpan, LiteralEscapeErrorKind.InvalidEscape, "trailing '\\' escape sequence"));
                i++;
                break;
            }

            char escChar = content[i + 1];
            char? simpleChar = escChar switch
            {
                '\'' => '\'',
                '"' => '"',
                '\\' => '\\',
                '0' => '\0',
                'a' => '\a',
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'v' => '\v',
                _ => null
            };

            if (simpleChar is { } decoded)
            {
                sb.Append(decoded);
                if (trackSourceSpans)
                    escapeSpans.Add(new TextSpan(contentStartOffset + escStart, 2));
                scalarCount++;
                i += 2;
                continue;
            }

            if (escChar == 'x')
            {
                if (i + 3 < content.Length && TryParseFixedHex(content.Slice(i + 2, 2), out uint byteVal))
                {
                    sb.Append((char)byteVal);
                    if (trackSourceSpans)
                        escapeSpans.Add(new TextSpan(contentStartOffset + escStart, 4));
                    scalarCount++;
                    i += 4;
                }
                else
                {
                    int len = 2;
                    while (len < 4 && i + len < content.Length && char.IsAsciiHexDigit(content[i + len]))
                        len++;
                    var errSpan = trackSourceSpans ? new TextSpan(contentStartOffset + escStart, len) : default;
                    (errors ??= []).Add(new LiteralEscapeError(errSpan, LiteralEscapeErrorKind.InvalidEscape, "'\\x' escape sequence requires two hexadecimal digits"));
                    sb.Append(content.Slice(escStart, len));
                    scalarCount++;
                    i += len;
                }
                continue;
            }

            if (escChar == 'u')
            {
                if (i + 2 < content.Length && content[i + 2] == '{')
                {
                    int closeBraceRel = content[(i + 3)..].IndexOf('}');
                    if (closeBraceRel < 0)
                    {
                        var errSpan = trackSourceSpans ? new TextSpan(contentStartOffset + escStart, content.Length - escStart) : default;
                        (errors ??= []).Add(new LiteralEscapeError(errSpan, LiteralEscapeErrorKind.InvalidEscape, "unterminated '\\u{...}' Unicode escape sequence"));
                        sb.Append(content[escStart..]);
                        scalarCount++;
                        break;
                    }

                    var hexBody = content.Slice(i + 3, closeBraceRel);
                    int totalLen = 3 + closeBraceRel + 1;
                    var span = trackSourceSpans ? new TextSpan(contentStartOffset + escStart, totalLen) : default;

                    if (hexBody.IsEmpty || !AreAllHexDigits(hexBody))
                    {
                        (errors ??= []).Add(new LiteralEscapeError(span, LiteralEscapeErrorKind.InvalidEscape, "Unicode escape '\\u{...}' must contain 1 to 6 hexadecimal digits"));
                    }
                    else if (hexBody.Length > 6 || !TryParseHexUInt64(hexBody, out ulong scalar) || scalar > 0x10FFFF || !Rune.TryCreate((uint)scalar, out var rune))
                    {
                        (errors ??= []).Add(new LiteralEscapeError(span, LiteralEscapeErrorKind.InvalidUnicodeEscape, "invalid Unicode scalar value in '\\u{...}' (must be U+0000..U+10FFFF excluding surrogates U+D800..U+DFFF)"));
                    }
                    else
                    {
                        sb.Append(rune.ToString());
                        if (trackSourceSpans)
                            escapeSpans.Add(span);
                    }

                    scalarCount++;
                    i += totalLen;
                    continue;
                }

                if (i + 5 < content.Length && TryParseFixedHex(content.Slice(i + 2, 4), out uint u16Code))
                {
                    var span = trackSourceSpans ? new TextSpan(contentStartOffset + escStart, 6) : default;
                    if (Rune.TryCreate(u16Code, out var rune))
                    {
                        sb.Append(rune.ToString());
                        if (trackSourceSpans)
                            escapeSpans.Add(span);
                    }
                    else
                    {
                        (errors ??= []).Add(new LiteralEscapeError(span, LiteralEscapeErrorKind.InvalidUnicodeEscape, $"invalid Unicode scalar value 'U+{u16Code:X4}' (surrogate code points U+D800..U+DFFF are not allowed)"));
                    }
                    scalarCount++;
                    i += 6;
                }
                else
                {
                    int len = 2;
                    while (len < 6 && i + len < content.Length && char.IsAsciiHexDigit(content[i + len]))
                        len++;
                    var errSpan = trackSourceSpans ? new TextSpan(contentStartOffset + escStart, len) : default;
                    (errors ??= []).Add(new LiteralEscapeError(errSpan, LiteralEscapeErrorKind.InvalidEscape, "'\\u' escape sequence requires four hexadecimal digits or braced '\\u{...}'"));
                    sb.Append(content.Slice(escStart, len));
                    scalarCount++;
                    i += len;
                }
                continue;
            }

            var unkSpan = trackSourceSpans ? new TextSpan(contentStartOffset + escStart, 2) : default;
            (errors ??= []).Add(new LiteralEscapeError(unkSpan, LiteralEscapeErrorKind.InvalidEscape, $"unrecognized escape sequence '\\{escChar}'"));
            sb.Append(escChar);
            scalarCount++;
            i += 2;
        }

        return new ResolvedLiteralInfo(
            sb.ToString(),
            scalarCount,
            escapeSpans.Count == 0 ? Array.Empty<TextSpan>() : escapeSpans,
            errors is null ? Array.Empty<LiteralEscapeError>() : errors);
    }

    private static bool AreAllHexDigits(ReadOnlySpan<char> chars)
    {
        foreach (char c in chars)
        {
            if (!char.IsAsciiHexDigit(c))
                return false;
        }
        return true;
    }

    private static bool TryParseFixedHex(ReadOnlySpan<char> digits, out uint value)
    {
        value = 0;
        foreach (char c in digits)
        {
            if (!TryHexDigit(c, out uint digit))
                return false;
            value = (value << 4) | digit;
        }
        return true;
    }

    private static bool TryParseHexUInt64(ReadOnlySpan<char> digits, out ulong value)
    {
        value = 0;
        foreach (char c in digits)
        {
            if (!TryHexDigit(c, out uint digit))
                return false;
            value = (value << 4) | digit;
        }
        return true;
    }

    private static bool TryHexDigit(char c, out uint digit)
    {
        if (c is >= '0' and <= '9') { digit = (uint)(c - '0'); return true; }
        if (c is >= 'a' and <= 'f') { digit = (uint)(c - 'a' + 10); return true; }
        if (c is >= 'A' and <= 'F') { digit = (uint)(c - 'A' + 10); return true; }
        digit = 0;
        return false;
    }
}
