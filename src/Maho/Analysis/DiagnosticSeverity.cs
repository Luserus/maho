namespace Maho;

/// <summary>
/// Severity levels exposed by the public analysis contract.
/// </summary>
public enum DiagnosticSeverity : byte
{
    /// <summary>
    /// Supplemental information that does not mark analysis as failing.
    /// </summary>
    Info,

    /// <summary>
    /// A noteworthy condition that still allows analysis to proceed.
    /// </summary>
    Warning,

    /// <summary>
    /// A failing problem that should contribute to an unsuccessful analysis result.
    /// </summary>
    Error
}
