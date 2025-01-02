namespace Maho.Text;

/// <summary> Represents a line of text in the source code. </summary>
/// <param name="text"> The source text. </param>
/// <param name="start"> The start position of the line. </param>
/// <param name="length"> The length of the line. </param>
/// <param name="lengthIncludingLineBreak"> The length of the line including the line break. </param>
internal readonly struct TextLine(SourceText text, int start, int length, int lengthIncludingLineBreak)
{
    /// <summary> Gets the source text. </summary>
    public SourceText Text { get; } = text;

    /// <summary> Gets the start position of the line. </summary>
    public int Start { get; } = start;

    /// <summary> Gets the length of the line. </summary>
    public int Length { get; } = length;

    /// <summary> Gets the length of the line including the line break. </summary>
    public int LengthIncludingLineBreak { get; } = lengthIncludingLineBreak;

    /// <summary> Gets the end position of the line. </summary>
    public int End => Start + Length;

    /// <summary> Gets the span of the line. </summary>
    public TextSpan Span => new(Start, Length);

    /// <summary> Gets the span of the line including the line break. </summary>
    public TextSpan SpanIncludingLineBreak => new(Start, LengthIncludingLineBreak);

    /// <summary> Gets the text of the line. </summary>
    /// <returns> The text of the line. </returns>
    public override string ToString() => Text.ToString(Span);
}
