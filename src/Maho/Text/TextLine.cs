namespace Maho.Text;

/// <summary> Represents a line of text in the source code. </summary>
internal readonly struct TextLine
{
    /// <summary> The source text. </summary>
    public SourceText Text { get; }
    /// <summary> The start position of the line. </summary>
    public int Start { get; }
    /// <summary> The length of the line. </summary>
    public int Length { get; }
    /// <summary> The length of the line including the line break. </summary>
    public int LengthIncludingLineBreak { get; }
    /// <summary> The end position of the line. </summary>
    public int End => Start + Length;
    /// <summary> The span of the line. </summary>
    public TextSpan Span => new(Start, Length);
    /// <summary> The span of the line including the line break. </summary>
    public TextSpan SpanIncludingLineBreak => new(Start, LengthIncludingLineBreak);

    /// <param name="text"> Source text. </param>
    /// <param name="start"> Start position of the line. </param>
    /// <param name="length"> Length of the line. </param>
    /// <param name="lengthIncludingLineBreak"> Length of the line including the line break. </param>
    public TextLine(SourceText text, int start, int length, int lengthIncludingLineBreak)
    {
        Text = text;
        Start = start;
        Length = length;
        LengthIncludingLineBreak = lengthIncludingLineBreak;
    }

    /// <summary> The text of the line. </summary>
    /// <returns> The text of the line as string. </returns>
    public override string ToString() => Text.ToString(Span);
}
