using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Maho.Text;

namespace Maho.Syntax;

/// <summary>
/// Implements Unicode Standard Annex #31 (UAX #31) identifier syntax.
/// Supports standard identifier start and continuation characters with fast ASCII paths
/// and full Unicode code point and surrogate pair support.
/// </summary>
internal static class UnicodeIdentifier
{
    /// <summary>
    /// Checks whether the character at <paramref name="index"/> in <paramref name="text"/> can start a valid identifier.
    /// If true, outputs the length in UTF-16 code units (1 or 2).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIdentifierStart(SourceText text, int index, out int length)
    {
        if (index >= text.Length)
        {
            length = 0;
            return false;
        }

        char ch = text[index];
        if (ch < 128)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || ch == '_')
            {
                length = 1;
                return true;
            }

            length = 0;
            return false;
        }

        if (char.IsHighSurrogate(ch) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            Rune rune = new Rune(ch, text[index + 1]);
            if (IsIdentifierStart(rune))
            {
                length = 2;
                return true;
            }

            length = 0;
            return false;
        }

        if (IsIdentifierStart(ch))
        {
            length = 1;
            return true;
        }

        length = 0;
        return false;
    }

    /// <summary>
    /// Checks whether the character at <paramref name="index"/> in <paramref name="text"/> can continue a valid identifier.
    /// If true, outputs the length in UTF-16 code units (1 or 2).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIdentifierContinue(SourceText text, int index, out int length)
    {
        if (index >= text.Length)
        {
            length = 0;
            return false;
        }

        char ch = text[index];
        if (ch < 128)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '_')
            {
                length = 1;
                return true;
            }

            length = 0;
            return false;
        }

        if (char.IsHighSurrogate(ch) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            Rune rune = new Rune(ch, text[index + 1]);
            if (IsIdentifierContinue(rune))
            {
                length = 2;
                return true;
            }

            length = 0;
            return false;
        }

        if (IsIdentifierContinue(ch))
        {
            length = 1;
            return true;
        }

        length = 0;
        return false;
    }

    /// <summary> Checks if the given character is a valid UAX #31 identifier start character. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIdentifierStart(char ch)
    {
        if (ch < 128)
            return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || ch == '_';

        return IsIdentifierStart(char.GetUnicodeCategory(ch)) || IsOtherIdStart(ch);
    }

    /// <summary> Checks if the given Rune is a valid UAX #31 identifier start character. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIdentifierStart(Rune rune)
    {
        if (rune.IsAscii)
        {
            char ch = (char)rune.Value;
            return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || ch == '_';
        }

        return IsIdentifierStart(Rune.GetUnicodeCategory(rune)) || IsOtherIdStart(rune.Value);
    }

    /// <summary> Checks if the given character is a valid UAX #31 identifier continuation character. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIdentifierContinue(char ch)
    {
        if (ch < 128)
            return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '_';

        return IsIdentifierContinue(char.GetUnicodeCategory(ch)) || IsOtherIdContinue(ch);
    }

    /// <summary> Checks if the given Rune is a valid UAX #31 identifier continuation character. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIdentifierContinue(Rune rune)
    {
        if (rune.IsAscii)
        {
            char ch = (char)rune.Value;
            return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '_';
        }

        return IsIdentifierContinue(Rune.GetUnicodeCategory(rune)) || IsOtherIdContinue(rune.Value);
    }

    /// <summary> Checks if a UnicodeCategory corresponds to an identifier start character. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIdentifierStart(UnicodeCategory category) => category switch
    {
        UnicodeCategory.UppercaseLetter => true,        // Lu
        UnicodeCategory.LowercaseLetter => true,        // Ll
        UnicodeCategory.TitlecaseLetter => true,        // Lt
        UnicodeCategory.ModifierLetter => true,         // Lm
        UnicodeCategory.OtherLetter => true,            // Lo
        UnicodeCategory.LetterNumber => true,           // Nl
        UnicodeCategory.ConnectorPunctuation => true,   // Pc (including '_')
        _ => false
    };

    /// <summary> Checks if a UnicodeCategory corresponds to an identifier continuation character. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIdentifierContinue(UnicodeCategory category) => category switch
    {
        UnicodeCategory.UppercaseLetter => true,        // Lu
        UnicodeCategory.LowercaseLetter => true,        // Ll
        UnicodeCategory.TitlecaseLetter => true,        // Lt
        UnicodeCategory.ModifierLetter => true,         // Lm
        UnicodeCategory.OtherLetter => true,            // Lo
        UnicodeCategory.LetterNumber => true,           // Nl
        UnicodeCategory.DecimalDigitNumber => true,     // Nd
        UnicodeCategory.ConnectorPunctuation => true,   // Pc (including '_')
        UnicodeCategory.NonSpacingMark => true,         // Mn
        UnicodeCategory.SpacingCombiningMark => true,   // Mc
        UnicodeCategory.Format => true,                 // Cf (e.g. ZWNJ, ZWJ)
        _ => false
    };

    /// <summary> UAX #31 Other_ID_Start code points. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsOtherIdStart(int codePoint) =>
        codePoint is (>= 0x1885 and <= 0x1886)
            or 0x2118
            or 0x212E
            or (>= 0x309B and <= 0x309C);

    /// <summary> UAX #31 Other_ID_Continue code points. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsOtherIdContinue(int codePoint) =>
        IsOtherIdStart(codePoint)
        || codePoint is 0x00B7
            or 0x0387
            or (>= 0x1369 and <= 0x1371)
            or 0x19DA;
}
