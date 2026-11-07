using System;

namespace Maho;

/// <summary>
/// Intentional placeholder exception thrown when compilation reaches an unimplemented stage.
/// Holds the compilation produced up to the boundary.
/// </summary>
public sealed class CompilerPipelineNotImplementedException : Exception
{
    /// <summary> The compilation produced prior to reaching the unimplemented stage. </summary>
    public Compilation Compilation { get; }

    public CompilerPipelineNotImplementedException(string message, Compilation compilation)
        : base(message)
    {
        Compilation = compilation;
    }
}
