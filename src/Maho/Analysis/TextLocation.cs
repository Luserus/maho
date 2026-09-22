namespace Maho;

/// <summary> One-based source coordinate used by the public analysis API. </summary>
/// <param name="Line">The human-facing line number.</param>
/// <param name="Column">The human-facing column number.</param>
public readonly record struct TextLocation(int Line, int Column);
