namespace Maho.Text;

/// <summary> Represents a span of text in the source code. </summary>
internal readonly struct TextSpan
{
    /// <summary> The start position of the span. </summary>
    public int Start { get; }
    /// <summary> The length of the span. </summary>
    public int Length { get; }
    /// <summary> The end position of the span. </summary>
    public int End => Start + Length;

    /// <param name="start"> Start position of the span. </param>
    /// <param name="length"> Length of the span. </param>
    public TextSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    /// <summary> Creates a new TextSpan from start and end positions. </summary>
    /// <param name="start"> The start position of the span. </param>
    /// <param name="end"> The end position of the span. </param>
    /// <returns> A new TextSpan </returns>
    public static TextSpan FromBounds(int start, int end) => new(start, end - start);

    /// <summary> Gets the line number of the start position. </summary>
    public int GetStartLine(SourceText sourceText) => sourceText.GetLineIndex(Start);

    /// <summary> Gets the column number of the start position. </summary>
    public int GetStartColumn(SourceText sourceText)
    {
        var lineIndex = sourceText.GetLineIndex(Start);
        var line = sourceText.Lines[lineIndex];
        return Start - line.Start;
    }

    /// <summary> Gets the line number of the end position. </summary>
    public int GetEndLine(SourceText sourceText) => sourceText.GetLineIndex(End);

    /// <summary> Gets the column number of the end position. </summary>
    public int GetEndColumn(SourceText sourceText)
    {
        var lineIndex = sourceText.GetLineIndex(End);
        var line = sourceText.Lines[lineIndex];
        return End - line.Start;
    }
}
