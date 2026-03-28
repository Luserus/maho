namespace Maho.Diagnostics;

/// <summary>
/// Internal severity categories used before diagnostics are projected into the public API model.
/// </summary>
internal enum DiagnosticKind : byte
{
    /// <summary>
    /// Supplemental information that does not mark analysis as failing.
    /// </summary>
    Info,

    /// <summary>
    /// A noteworthy condition that still allows analysis to continue.
    /// </summary>
    Warning,

    /// <summary>
    /// A failing diagnostic that should contribute to an unsuccessful analysis result.
    /// </summary>
    Error
}
