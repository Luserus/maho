namespace Miryo.Build;

/// <summary>
/// Configuration parsed from a Miryo workspace file (<c>.mrws</c>).
/// </summary>
public sealed class MiryoWorkspaceConfiguration
{
    /// <summary>
    /// Optional logical name of the workspace.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Relative paths or glob patterns specifying member projects (<c>.mhpr</c>), project directories, or nested workspaces (<c>.mrws</c>).
    /// </summary>
    public string[] Members { get; init; } = [];

    /// <summary>
    /// Names of projects to build by default when no specific project is targeted on the command line.
    /// </summary>
    public string[] Default { get; init; } = [];

    /// <summary>
    /// Relative path glob patterns to exclude from member discovery.
    /// </summary>
    public string[] Exclude { get; init; } = [];

    /// <summary>
    /// Workspace-wide shared configuration inherited by member projects.
    /// </summary>
    public MiryoWorkspaceSharedConfiguration? Shared { get; init; }

    /// <summary>
    /// Optional workspace root target output directory (defaults to <c>target</c>).
    /// </summary>
    public string? TargetDirectory { get; init; }
}
