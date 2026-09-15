namespace Maho;

/// <summary>
/// Top-level result returned by compiler-owned batch analysis. The compiler controls file-level
/// parallelism internally, then exposes ordered per-file outcomes through this immutable payload.
/// </summary>
/// <param name="ProjectName">Friendly identity for the analyzed batch.</param>
/// <param name="Files">Ordered per-file analysis outcomes.</param>
public sealed record CompilerProjectAnalysisResult(
    string ProjectName,
    CompilerBatchFileResult[] Files)
{
    /// <summary> Selected explicit or implicit entry source file, when project analysis found one. </summary>
    public string? EntryFile { get; init; }

    /// <summary> Indicates whether any file in the batch reported errors or analysis failure. </summary>
    public bool HasErrors
    {
        get
        {
            for (int i = 0; i < Files.Length; i++)
            {
                if (Files[i].HasErrors)
                    return true;
            }

            return false;
        }
    }
}
