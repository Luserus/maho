namespace Maho;

/// <summary>
/// Captures the outcome of analyzing one file inside a compiler-owned batch run without forcing the
/// whole batch to stop on the first failure.
/// </summary>
/// <param name="SourcePath">Stable source identity used for the analyzed file.</param>
/// <param name="Analysis">Successful compiler result when analysis completed.</param>
/// <param name="AnalysisError">Formatted failure text when analysis did not complete.</param>
/// <param name="IsInternalError">Whether the failure should be treated as a compiler fault.</param>
/// <param name="HasErrors">Whether this file contributes to a failing batch.</param>
public sealed record CompilerBatchFileResult(
    string SourcePath,
    CompilerAnalysisResult? Analysis,
    string? AnalysisError,
    bool IsInternalError,
    bool HasErrors);
