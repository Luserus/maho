using System.Collections.Generic;

namespace Maho.Text;

/// <summary> Represents the source text of the program. </summary>
internal sealed class SourceText
{
    /// <summary> The source text. </summary>
    private readonly string text;

    /// <summary> The length of the source text. </summary>
    public int Length => text.Length;
    /// <summary> The lines of the source text. </summary>
    public TextLine[] Lines { get; }

    /// <summary> Initializes a new instance of the <see cref="SourceText"/> class. </summary>
    /// <param name="text"> Source text. </param>
    public SourceText(string text)
    {
        this.text = text;
        Lines = ParseLines();
    }

    /// <summary> Gets the character at the specified position. </summary>
    /// <param name="position"> The position of the character. </param>
    /// <returns> The character at the specified position. </returns>
    public char this[int position] => text[position];

    /// <summary> Parses the lines of the source text. </summary>
    /// <returns> An array of <see cref="TextLine"/>. </returns>
    private TextLine[] ParseLines()
    {
        List<TextLine> result = [];

        var position = 0;
        var lineStart = 0;

        while (position < text.Length)
        {
            var lineBreakWidth = GetLineBreakWidth(position);

            if (lineBreakWidth == 0)
                position++;
            else
            {
                AddLine(result, position, lineStart, lineBreakWidth);
                position += lineBreakWidth;
                lineStart = position;
            }
        }

        if (position >= lineStart)
            AddLine(result, position, lineStart, 0);

        return [.. result];
    }

    /// <summary> Adds a line to the result. </summary>
    /// <param name="result"> The result builder. </param>
    /// <param name="position"> The current position. </param>
    /// <param name="lineStart"> The start of the line. </param>
    /// <param name="lineBreakWidth"> The width of the line break. </param>
    private void AddLine(List<TextLine> result, int position, int lineStart, int lineBreakWidth)
    {
        var lineLength = position - lineStart;
        var lineLengthIncludingLineBreak = lineLength + lineBreakWidth;
        var line = new TextLine(this, lineStart, lineLength, lineLengthIncludingLineBreak);
        result.Add(line);
    }

    /// <summary> Gets the width of the line break at the specified position. </summary>
    /// <param name="position"> The position. </param>
    /// <returns> The width of the line break. </returns>
    private int GetLineBreakWidth(int position)
    {
        var ch = text[position];
        var next = position + 1 >= text.Length ? '\0' : text[position + 1];

        if (ch == '\r' && next == '\n')
            return 2;

        if (ch == '\r' || next == '\n')
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

    /// <summary> Gets the text as string. </summary>
    /// <returns> The text. </returns>
    public override string ToString() => text;

    /// <summary> Gets the text within the specified span. </summary>
    /// <param name="span"> The span of text to get. </param>
    /// <returns> The text within the specified span. </returns>
    public string ToString(TextSpan span) => text.Substring(span.Start, span.Length);
}
