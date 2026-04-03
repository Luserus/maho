using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Maho.Text;

/// <summary>
/// Represents the source text of the program.
/// Supports two load modes:
///   Eager       — file is decoded immediately into a string at construction time.
///   LazyCached  — file is decoded into a string on first access then cached.
/// </summary>
internal sealed class SourceText : IDisposable
{
    private string? cachedText;                         // decoded string, null until loaded in LazyCached mode
    private readonly MemoryMappedFile? mmf;             // memory-mapped file, kept open for LazyCached lazy decode
    private readonly MemoryMappedViewAccessor? accessor;
    private readonly long fileLength;                   // raw byte length of the file
    private readonly SourceTextLoadMode loadMode;
    private TextLine[]? lazyLines;                      // line table, parsed on first access

    // --- Public properties ---

    /// <summary> All lines in the source text. Parsed lazily on first access. </summary>
    public TextLine[] Lines => lazyLines ?? ParseLines();

    /// <summary> Character length of the source text. </summary>
    public int Length => EnsureText().Length;

    /// <summary> Character at the given position. </summary>
    public char this[int position] => EnsureText()[position];

    // --- Constructors ---

    /// <summary> Loads source text from a file. </summary>
    public SourceText(SourceFile sourceFile)
    {
        if (!File.Exists(sourceFile.FilePath))
            throw new FileNotFoundException("Source file not found", sourceFile.FilePath);

        loadMode = sourceFile.LoadMode;

        var fi = new FileInfo(sourceFile.FilePath);
        fileLength = fi.Length;

        if (fileLength == 0)
        {
            // Empty files bypass the memory-mapped path entirely so downstream code still sees a
            // fully initialized text object with a consistent empty-line table.
            cachedText = string.Empty;
            lazyLines = [];
            return;
        }

        mmf = MemoryMappedFile.CreateFromFile(
            sourceFile.FilePath, FileMode.Open, null, 0L, MemoryMappedFileAccess.Read);
        accessor = mmf.CreateViewAccessor(0, fileLength, MemoryMappedFileAccess.Read);

        if (loadMode is SourceTextLoadMode.Eager)
        {
            // Eager mode pays the decode cost once up front and immediately releases the OS-backed
            // mapping so the rest of analysis works against a normal managed string.
            cachedText = DecodeFromAccessor();
            lazyLines = ParseLines();
            accessor.Dispose();
            mmf.Dispose();
        }
        // LazyCached keeps the mapping alive until somebody actually asks for decoded characters.
    }

    /// <summary> Wraps an already-decoded in-memory string. Always eager. </summary>
    public SourceText(string text)
    {
        cachedText = text ?? string.Empty;
        loadMode = SourceTextLoadMode.Eager;
        lazyLines = ParseLines();
    }

    /// <summary>
    /// Returns true if the characters at [position, position + value.Length)
    /// exactly match value, without allocating a substring.
    /// </summary>
    public bool MatchesAt(int position, ReadOnlySpan<char> value)
    {
        var text = EnsureText();

        if (position < 0 || position + value.Length > text.Length)
            return false;

        for (int i = 0; i < value.Length; i++)
        {
            if (text[position + i] != value[i])
                return false;
        }

        return true;
    }

    public override string ToString() => EnsureText();

    public string ToString(TextSpan span)
    {
        if (span.Length == 0)
            return string.Empty;

        return EnsureText().Substring(span.Start, span.Length);
    }

    // --- Private helpers ---

    /// <summary>
    /// Returns the decoded text, decoding from the memory-mapped file if this is
    /// LazyCached and the text hasn't been decoded yet.
    /// </summary>
    private string EnsureText()
    {
        if (cachedText is not null)
            return cachedText;

        // The first text read in LazyCached mode flips the object into a normal cached-string view.
        cachedText = DecodeFromAccessor();
        return cachedText;
    }

    /// <summary> Reads the entire file from the memory-mapped accessor and decodes as UTF-8. </summary>
    private string DecodeFromAccessor()
    {
        var buffer = new byte[fileLength];
        accessor!.ReadArray(0, buffer, 0, (int)fileLength);
        return Encoding.UTF8.GetString(buffer);
    }

    // --- Line parsing ---

    private TextLine[] ParseLines()
    {
        var text = EnsureText();
        var lines = new List<TextLine>();
        int position = 0;
        int lineStart = 0;

        while (position < text.Length)
        {
            int breakWidth = GetLineBreakWidth(text, position);

            if (breakWidth == 0)
            {
                position++;
                continue;
            }

            AddLine(lines, position, lineStart, breakWidth);
            position += breakWidth;
            lineStart = position;
        }

        // Keep the trailing unterminated line addressable; diagnostics rely on this behavior.
        if (position >= lineStart)
            AddLine(lines, position, lineStart, 0);

        lazyLines = [.. lines];
        return lazyLines;
    }

    private void AddLine(List<TextLine> lines, int position, int start, int breakWidth)
    {
        int length = position - start;
        int lengthIncludingBreak = length + breakWidth;
        lines.Add(new TextLine(this, start, length, lengthIncludingBreak));
    }

    private static int GetLineBreakWidth(string text, int position)
    {
        char ch = text[position];
        char next = position + 1 < text.Length ? text[position + 1] : '\0';

        if (ch == '\r' && next == '\n') return 2;  // CRLF
        if (ch == '\r' || ch == '\n') return 1;    // CR or LF

        return 0;
    }

    // --- Line index lookup ---

    /// <summary>
    /// Returns the zero-based line index for the given character position
    /// using binary search over the line table.
    /// </summary>
    public int GetLineIndex(int position)
    {
        var lines = Lines; // Forces line parsing once, then reuses the cached table for all lookups.

        int lower = 0;
        int upper = lines.Length - 1;

        // Offset-to-line lookup is on hot paths for diagnostics and spans, so use binary search
        // rather than scanning the line table from the start.
        while (lower <= upper)
        {
            int index = lower + ((upper - lower) >> 1);
            int start = lines[index].Start;

            if (position == start) return index;
            if (position < start) upper = index - 1;
            else lower = index + 1;
        }

        return Math.Max(0, lower - 1);
    }

    void IDisposable.Dispose()
    {
        accessor?.Dispose();
        mmf?.Dispose();
    }
}