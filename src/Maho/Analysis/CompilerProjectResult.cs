using System.Collections.Generic;
using System.Linq;

namespace Maho;

/// <summary>
/// Captures the outcome of compiling one file inside a compiler-owned batch or project run.
/// Holds the polymorphic <see cref="CompilationOutput"/> (debug, IL, or diagnostics).
/// </summary>
public sealed record CompilerBatchFileResult(
    string SourcePath,
    CompilationOutput? Output,
    string? AnalysisError,
    bool IsInternalError,
    bool HasErrors)
{
    /// <summary>
    /// Convenient accessor for when the output is a <see cref="DebugCompilationOutput"/>.
    /// </summary>
    public DebugCompilationOutput? DebugOutput => Output as DebugCompilationOutput;

    /// <summary>
    /// Backward-compatible property alias for debug analysis payloads.
    /// </summary>
    public DebugCompilationOutput? Analysis => Output as DebugCompilationOutput;

    public CompilerBatchFileResult(
        string sourcePath,
        CompilationOutput? output,
        string? analysisError = null,
        bool isInternalError = false)
        : this(sourcePath, output, analysisError, isInternalError, analysisError != null || (output?.HasErrors ?? false))
    {
    }
}

/// <summary>
/// Top-level result returned by compiler-owned batch or project analysis and compilation.
/// </summary>
public sealed record CompilerProjectAnalysisResult(
    string ProjectName,
    CompilerBatchFileResult[] Files)
{
    /// <summary> Selected explicit or implicit entry source file, when project analysis found one. </summary>
    public string? EntryFile { get; init; }

    /// <summary> The underlying compilation, when successfully initialized. </summary>
    public Compilation? Compilation { get; init; }

    /// <summary> Aggregated diagnostics across all files in the project. </summary>
    public IReadOnlyList<DiagnosticInfo> Diagnostics =>
        Compilation?.Diagnostics ?? Files.SelectMany(f => f.Output?.Diagnostics ?? []).ToList();

    /// <summary> Indicates whether any file in the batch reported errors or analysis failure. </summary>
    public bool HasErrors => Files.Any(f => f.HasErrors);
}
