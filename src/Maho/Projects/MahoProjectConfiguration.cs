using System.Collections.Generic;

namespace Maho;

/// <summary> Configuration read from one domain-specific <c>.mhpr</c> project file. </summary>
public sealed class MahoProjectConfiguration
{
    /// <summary> Optional source file selected as the project's explicit entry point. </summary>
    public string? EntryFile { get; init; }
    /// <summary> Whether unsafe operations are enabled project-wide. </summary>
    public bool GlobalUnsafeEnabled { get; init; }
    /// <summary> Whether implicit top-level statements are allowed for the project's entry file. </summary>
    public bool ImplicitTopLevel { get; init; }
    /// <summary> Referenced project paths, retained for project-graph resolution. </summary>
    public string[] ProjectsReferenced { get; init; } = [];
    /// <summary> Project-wide alias declarations, retained for alias resolution. </summary>
    public Dictionary<string, string> GlobalAliases { get; init; } = [];
}