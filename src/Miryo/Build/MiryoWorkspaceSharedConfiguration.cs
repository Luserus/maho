using System.Collections.Generic;

namespace Miryo.Build;

/// <summary>
/// Workspace-level configuration properties that member projects can inherit.
/// </summary>
public sealed class MiryoWorkspaceSharedConfiguration
{
    /// <summary>
    /// Shared global type aliases to apply across all member projects.
    /// </summary>
    public Dictionary<string, string> GlobalAliases { get; init; } = [];

    /// <summary>
    /// Default project-wide unsafe permission for member projects that do not explicitly set it.
    /// </summary>
    public bool? GlobalUnsafeEnabled { get; init; }
}

