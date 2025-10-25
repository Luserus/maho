using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Maho.Text;

/// <summary> Represents the source text of the program. </summary>
internal sealed class SourceText : IDisposable
{
    private readonly string? eagerText;                  // Eager or LazyCached decoded string
    private readonly MemoryMappedFile? mmf;             // Memory-mapped file for lazy modes
    private readonly MemoryMappedViewAccessor? accessor;
    private readonly long fileLength;
    private readonly SourceTextLoadMode loadMode;
    private readonly Decoder? utf8Decoder;                        // For LazyStreaming decoding
    private TextLine[]? lazyLines;                  // Lazy-parsed line info

    public TextLine[] Lines => lazyLines ?? ParseLines();

    public int Length => loadMode == SourceTextLoadMode.Eager || loadMode == SourceTextLoadMode.LazyCached
        ? eagerText!.Length
        : (int)fileLength; // approx; streaming actual length is dynamic

    // Constructor for file-based SourceText
    public SourceText(SourceFile sourceFile)
    {   
        if (!File.Exists(sourceFile.FilePath)) 
            throw new FileNotFoundException("Source file not found", sourceFile.FilePath);

        loadMode = sourceFile.LoadMode;
        var fi = new FileInfo(sourceFile.FilePath);
        fileLength = fi.Length;

        if (fileLength == 0)
        {
            eagerText = string.Empty;
            lazyLines = [];
            return;
        }

        switch (loadMode)
        {
            case SourceTextLoadMode.Eager:
            case SourceTextLoadMode.LazyCached:
                mmf = MemoryMappedFile.CreateFromFile(sourceFile.FilePath, FileMode.Open, null, 0L, MemoryMappedFileAccess.Read);
                accessor = mmf.CreateViewAccessor(0, fileLength, MemoryMappedFileAccess.Read);
                eagerText = ReadFullStringFromAccessor();
                lazyLines = ParseLines();
                break;

            case SourceTextLoadMode.LazyStreaming:
                mmf = MemoryMappedFile.CreateFromFile(sourceFile.FilePath, FileMode.Open, null, 0L, MemoryMappedFileAccess.Read);
                accessor = mmf.CreateViewAccessor(0, fileLength, MemoryMappedFileAccess.Read);
                utf8Decoder = Encoding.UTF8.GetDecoder();
                lazyLines = null; // will parse lines lazily
                break;

            default:
                break;
        }
    }

    // Constructor from in-memory string
    public SourceText(string text)
    {
        eagerText = text ?? string.Empty;
        loadMode = SourceTextLoadMode.Eager;
        lazyLines = ParseLines();
    }

    // Indexer
    public char this[int position] => loadMode switch
    {
        SourceTextLoadMode.Eager => eagerText![position],
        SourceTextLoadMode.LazyCached => eagerText![position],
        SourceTextLoadMode.LazyStreaming => ReadCharStreaming(position),
        _ => throw new InvalidOperationException("Unknown load mode")
    };

    public override string ToString() => loadMode switch
    {
        SourceTextLoadMode.Eager => eagerText!,
        SourceTextLoadMode.LazyCached => eagerText!,
        SourceTextLoadMode.LazyStreaming => ReadAllStreaming(),
        _ => throw new InvalidOperationException("Unknown load mode")
    };

    public string ToString(TextSpan span)
    {
        if (span.Length == 0) return string.Empty;
        return loadMode switch
        {
            SourceTextLoadMode.Eager => eagerText!.Substring(span.Start, span.Length),
            SourceTextLoadMode.LazyCached => eagerText!.Substring(span.Start, span.Length),
            SourceTextLoadMode.LazyStreaming => ReadSpanStreaming(span),
            _ => throw new InvalidOperationException("Unknown load mode")
        };
    }

    // --- LazyStreaming helpers ---
    private char ReadCharStreaming(int position)
    {
        if (position < 0 || position >= fileLength) 
            throw new ArgumentOutOfRangeException(nameof(position));

        byte[] buffer = new byte[4]; // max UTF-8 char length
        accessor!.ReadArray(position, buffer, 0, 1); // read first byte
        int charBytes = 1;

        // Determine byte count for this UTF-8 character
        byte first = buffer[0];
        if ((first & 0b1000_0000) == 0) charBytes = 1;
        else if ((first & 0b1110_0000) == 0b1100_0000) charBytes = 2;
        else if ((first & 0b1111_0000) == 0b1110_0000) charBytes = 3;
        else if ((first & 0b1111_1000) == 0b1111_0000) charBytes = 4;
        else throw new InvalidDataException($"Invalid UTF-8 start byte at position {position}");

        if (charBytes > 1)
            accessor.ReadArray(position, buffer, 0, charBytes);

        char[] chars = new char[2]; // max 2 chars for surrogate pair
        int count = utf8Decoder!.GetChars(buffer, 0, charBytes, chars, 0);
        return chars[0];
    }

    private string ReadSpanStreaming(TextSpan span)
    {
        if (span.Length == 0) 
            return string.Empty;

        byte[] buffer = new byte[span.Length];
        accessor!.ReadArray(span.Start, buffer, 0, span.Length);

        return Encoding.UTF8.GetString(buffer);
    }

    private string ReadAllStreaming()
    {
        byte[] buffer = new byte[fileLength];
        accessor!.ReadArray(0, buffer, 0, (int)fileLength);
        return Encoding.UTF8.GetString(buffer);
    }

    // --- Eager/LazyCached ---
    private string ReadFullStringFromAccessor()
    {
        byte[] buffer = new byte[fileLength];
        accessor!.ReadArray(0, buffer, 0, (int)fileLength);
        return Encoding.UTF8.GetString(buffer);
    }

    // --- Line parsing ---
    private TextLine[] ParseLines()
    {
        var lines = new List<TextLine>();
        int position = 0;
        int lineStart = 0;
        var textToParse = loadMode == SourceTextLoadMode.LazyStreaming ? ReadAllStreaming() : eagerText!;

        while (position < textToParse.Length)
        {
            int breakWidth = GetLineBreakWidth(textToParse, position);
            if (breakWidth == 0) { position++; continue; }

            AddLine(lines, position, lineStart, breakWidth);
            position += breakWidth;
            lineStart = position;
        }

        if (position >= lineStart)
            AddLine(lines, position, lineStart, 0);

        lazyLines = [.. lines];
        
        return [.. lines];
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
        char next = (position + 1 >= text.Length) ? '\0' : text[position + 1];

        if (ch == '\r' && next == '\n') 
            return 2;
        if (ch == '\r' || ch == '\n')
            return 1;
            
        return 0;
    }

    public int GetLineIndex(int position)
    {
        if (lazyLines == null) 
            ParseLines();

        int lower = 0;
        int upper = lazyLines!.Length - 1;

        while (lower <= upper)
        {
            int index = lower + ((upper - lower) >> 1);
            int start = lazyLines[index].Start;

            if (position == start)
                return index;
            else if (position < start) 
                upper = index - 1;
            else
                lower = index + 1;
        }

        return Math.Max(0, lower - 1);
    }

    public void Dispose()
    {
        accessor?.Dispose();
        mmf?.Dispose();
    }
}