namespace Maho;

using Maho.Diagnostics;

/// <summary>
/// Public projection of a diagnostic label (caret underline or secondary annotation).
/// </summary>
/// <param name="Span">Location of the label in the source text.</param>
/// <param name="Message">Optional message attached to this label.</param>
/// <param name="Style">Primary or secondary styling.</param>
/// <param name="FilePath">Optional path to the source file containing this label.</param>
public readonly record struct DiagnosticLabelInfo(
    TextSpanInfo Span,
    string? Message,
    DiagnosticLabelStyle Style,
    string? FilePath = null);
