namespace Maho;

using Maho.Syntax;
using Maho.Text;

/// <summary>
/// Public span contract that pairs absolute offsets with one-based line/column endpoints so
/// callers can render locations without holding onto the original source buffer.
/// </summary>
/// <param name="Start">Zero-based inclusive start offset.</param>
/// <param name="Length">Span length in characters.</param>
/// <param name="End">Zero-based exclusive end offset.</param>
/// <param name="StartLocation">One-based start location.</param>
/// <param name="EndLocation">One-based end location.</param>
public readonly record struct TextSpanInfo(int Start, int Length, int End, TextLocation StartLocation, TextLocation EndLocation)
{
    /// <summary>
    /// Creates a <see cref="TextSpanInfo"/> from an internal <see cref="TextSpan"/> and an optional <see cref="SourceText"/>.
    /// </summary>
    internal static TextSpanInfo FromSpan(TextSpan span, SourceText? text)
    {
        if (text is null)
        {
            return new TextSpanInfo(
                span.Start,
                span.Length,
                span.End,
                new TextLocation(1, span.Start + 1),
                new TextLocation(1, span.End + 1));
        }

        return new TextSpanInfo(
            span.Start,
            span.Length,
            span.End,
            new TextLocation(span.GetStartLine(text) + 1, span.GetStartColumn(text) + 1),
            new TextLocation(span.GetEndLine(text) + 1, span.GetEndColumn(text) + 1));
    }
}
