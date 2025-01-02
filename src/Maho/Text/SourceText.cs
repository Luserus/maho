using System.Collections.Generic;

namespace Maho.Text;

/// <summary> Represents the source text of the program. </summary>
internal sealed class SourceText
{
    /// <summary> The source text. </summary>
    private readonly string text;

    /// <summary> Initializes a new instance of the <see cref="SourceText"/> class. </summary>
    /// <param name="text"> The source text. </param>
    public SourceText(string text)
    {
        this.text = text;
        Lines = ParseLines(this, text);
    }

    /// <summary> Gets the length of the source text. </summary>
    public int Length => text.Length;

    /// <summary> Gets the lines of the source text. </summary>
    public TextLine[] Lines { get; }

    /// <summary> Gets the character at the specified position. </summary>
    /// <param name="position"> The position of the character. </param>
    /// <returns> The character at the specified position. </returns>
    public char this[int position] => text[position];

    /// <summary> Gets the text within the specified span. </summary>
    /// <param name="span"> The span of text to get. </param>
    /// <returns> The text within the specified span. </returns>
    public string ToString(TextSpan span) => text.Substring(span.Start, span.Length);

    /// <summary> Parses the lines of the source text. </summary>
    /// <param name="sourceText"> The source text. </param>
    /// <param name="text"> The text to parse. </param>
    /// <returns> An array of <see cref="TextLine"/>. </returns>
    private static TextLine[] ParseLines(SourceText sourceText, string text)
    {
        List<TextLine> result = [];

        var position = 0;
        var lineStart = 0;

        while (position < text.Length)
        {
            var lineBreakWidth = GetLineBreakWidth(text, position);

            if (lineBreakWidth == 0)
            {
                position++;
            }
            else
            {
                AddLine(result, sourceText, position, lineStart, lineBreakWidth);
                position += lineBreakWidth;
                lineStart = position;
            }
        }

        if (position >= lineStart)
        {
            AddLine(result, sourceText, position, lineStart, 0);
        }

        return [.. result];
    }

    /// <summary> Adds a line to the result. </summary>
    /// <param name="result"> The result builder. </param>
    /// <param name="sourceText"> The source text. </param>
    /// <param name="position"> The current position. </param>
    /// <param name="lineStart"> The start of the line. </param>
    /// <param name="lineBreakWidth"> The width of the line break. </param>
    private static void AddLine(List<TextLine> result, SourceText sourceText, int position, int lineStart, int lineBreakWidth)
    {
        var lineLength = position - lineStart;
        var lineLengthIncludingLineBreak = lineLength + lineBreakWidth;
        var line = new TextLine(sourceText, lineStart, lineLength, lineLengthIncludingLineBreak);
        result.Add(line);
    }

    /// <summary> Gets the width of the line break at the specified position. </summary>
    /// <param name="text"> The text. </param>
    /// <param name="position"> The position. </param>
    /// <returns> The width of the line break. </returns>
    private static int GetLineBreakWidth(string text, int position)
    {
        var c = text[position];
        var l = position + 1 >= text.Length ? '\0' : text[position + 1];

        if (c == '\r' && l == '\n')
            return 2;

        if (c == '\r' || c == '\n')
            return 1;

        return 0;
    }

    /// <summary> Gets the line index for the specified position. </summary>
    public int GetLineIndex(int position)
    {
        var lower = 0;
        var upper = Lines.Length - 1;

        while (lower <= upper)
        {
            var index = lower + (upper - lower) / 2;
            var start = Lines[index].Start;

            if (position == start)
                return index;

            if (position < start)
                upper = index - 1;
            else
                lower = index + 1;
        }

        return lower - 1;
    }
}
