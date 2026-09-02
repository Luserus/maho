namespace Maho;

/// <summary>
/// Public span contract that pairs absolute offsets with one-based line/column endpoints so
/// callers can render locations without holding onto the original source buffer.
/// </summary>
/// <param name="Start">Zero-based inclusive start offset.</param>
/// <param name="Length">Span length in characters.</param>
/// <param name="End">Zero-based exclusive end offset.</param>
/// <param name="StartLocation">One-based start location.</param>
/// <param name="EndLocation">One-based end location.</param>
public record struct TextSpanInfo(int Start, int Length, int End, TextLocation StartLocation, TextLocation EndLocation);
