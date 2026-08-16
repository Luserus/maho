using System;

namespace Maho;

/// <summary>
/// Indicates that front-end analysis completed successfully but the next compiler stage has not
/// been implemented yet. The completed analysis is retained for diagnostics and debug output.
/// </summary>
public sealed class CompilerPipelineNotImplementedException : Exception
{
    /// <summary> Completed front-end analysis that reached the unimplemented pipeline boundary. </summary>
    public CompilerProjectAnalysisResult Analysis { get; }

    internal CompilerPipelineNotImplementedException(CompilerProjectAnalysisResult analysis)
        : base("The lowering and code-generation pipeline has not been implemented.")
    {
        Analysis = analysis;
    }
}
