namespace Maho;

/// <summary>
/// Public projection of an informational note attached to a diagnostic.
/// </summary>
/// <param name="Message">Note message.</param>
/// <param name="FilePath">Optional path to the related source file.</param>
/// <param name="Span">Optional span reference for this note.</param>
public readonly record struct DiagnosticNoteInfo(
    string Message,
    string? FilePath = null,
    TextSpanInfo? Span = null);
