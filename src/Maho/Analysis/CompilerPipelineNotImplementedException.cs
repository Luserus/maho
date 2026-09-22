using System;

namespace Maho;

/// <summary>
/// Intentional placeholder exception thrown when compilation reaches an unimplemented stage.
/// Holds the analysis outcome produced up to the boundary.
/// </summary>
public sealed class CompilerPipelineNotImplementedException : Exception
{
    /// <summary> The analysis outcome produced prior to reaching the unimplemented stage. </summary>
    public CompilerProjectAnalysisResult Analysis { get; }

    public CompilerPipelineNotImplementedException(string message, CompilerProjectAnalysisResult analysis)
        : base(message)
    {
        Analysis = analysis;
    }
}
