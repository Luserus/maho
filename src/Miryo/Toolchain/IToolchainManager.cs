using System.Collections.Generic;

namespace Miryo.Toolchain;

/// <summary>
/// Abstraction for managing Maho toolchains, finding tools, and managing shims.
/// </summary>
public interface IToolchainManager
{
    /// <summary> Gets the active major.minor toolchain version. </summary>
    string ActiveVersion { get; }

    /// <summary> Gets the list of installed major.minor tool versions. </summary>
    IReadOnlyList<string> InstalledVersions { get; }

    /// <summary>
    /// Locates an executable tool (e.g. mahoc, maho-server, maho-ls) for the given version.
    /// If version is null, resolves for the active version.
    /// </summary>
    string? FindTool(string toolName, string? version = null);

    /// <summary>
    /// Updates shims in ~/.maho/shims to point to the active or specified toolchain version.
    /// </summary>
    void UpdateShims(string? version = null);
}
