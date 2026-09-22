namespace Maho;

/// <summary>
/// Public projection of a help message attached to a diagnostic.
/// </summary>
/// <param name="Message">Help message.</param>
/// <param name="FilePath">Optional path to the related source file.</param>
/// <param name="Span">Optional span reference for this help message.</param>
public readonly record struct DiagnosticHelpInfo(
    string Message,
    string? FilePath = null,
    TextSpanInfo? Span = null);
