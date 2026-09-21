namespace Maho.Build;

/// <summary>
/// Configuration specifying how source files are discovered and selected for a project.
/// </summary>
public sealed class MahoProjectSourcesConfiguration
{
    /// <summary>
    /// Base source directory ("$" or empty/null represents the project directory; relative paths resolve against the project directory).
    /// </summary>
    public string? Directory { get; init; }

    /// <summary>
    /// Explicit list of source file paths (relative to <see cref="Directory"/> or the project directory).
    /// </summary>
    public string[] SourceFiles { get; init; } = [];

    /// <summary>
    /// Optional file search pattern (e.g. <c>"*.mh"</c>).
    /// </summary>
    public string? ByName { get; init; }
}
