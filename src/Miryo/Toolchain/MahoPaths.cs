using System;
using System.IO;

namespace Miryo.Toolchain;

/// <summary>
/// Resolves standard toolchain locations within the ~/.maho hierarchy.
/// Owned and managed by Miryo as the ultimate toolchain manager.
/// </summary>
public static class MahoPaths
{
    /// <summary>
    /// Resolves the root directory of the Maho toolchain installation (~/.maho).
    /// Supports MAHO_HOME override and co-located development/dist directories.
    /// </summary>
    public static string GetMahoHome()
    {
        string? env = Environment.GetEnvironmentVariable("MAHO_HOME");
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);

        // Check if running inside a self-contained installation or dist layout
        string baseDir = AppContext.BaseDirectory;
        if (Directory.Exists(Path.Combine(baseDir, "tools")) &&
            (Directory.Exists(Path.Combine(baseDir, "compiler")) ||
             Directory.Exists(Path.Combine(baseDir, "sdk")) ||
             Directory.Exists(Path.Combine(baseDir, "runtime")) ||
             Directory.Exists(Path.Combine(baseDir, "server"))))
            return Path.GetFullPath(baseDir);

        string parentDir = Path.GetDirectoryName(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? baseDir;
        if (Directory.Exists(Path.Combine(parentDir, "tools")) &&
            (Directory.Exists(Path.Combine(parentDir, "compiler")) ||
             Directory.Exists(Path.Combine(parentDir, "sdk")) ||
             Directory.Exists(Path.Combine(parentDir, "runtime")) ||
             Directory.Exists(Path.Combine(parentDir, "server"))))
            return Path.GetFullPath(parentDir);

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".maho");
    }

    /// <summary> Bin directory: ~/.maho/bin </summary>
    public static string GetBinDir(string? home = null) => Path.Combine(home ?? GetMahoHome(), "bin");

    /// <summary> Tools root directory: ~/.maho/tools </summary>
    public static string GetToolsDir(string? home = null) => Path.Combine(home ?? GetMahoHome(), "tools");

    /// <summary> Specific versioned tools directory: ~/.maho/tools/{version} </summary>
    public static string GetToolDir(string version, string? home = null) =>
        Path.Combine(GetToolsDir(home), NormalizeVersion(version));

    /// <summary> Shims directory: ~/.maho/shims </summary>
    public static string GetShimsDir(string? home = null) => Path.Combine(home ?? GetMahoHome(), "shims");

    /// <summary> Compiler root directory: ~/.maho/compiler </summary>
    public static string GetCompilerDir(string? home = null) => Path.Combine(home ?? GetMahoHome(), "compiler");

    /// <summary> Specific versioned compiler directory: ~/.maho/compiler/{version} </summary>
    public static string GetCompilerDir(string version, string? home = null) =>
        Path.Combine(GetCompilerDir(home), NormalizeVersion(version));

    /// <summary> Specific versioned compiler directory (alias): ~/.maho/compiler/{version} </summary>
    public static string GetCompilerSdkDir(string version, string? home = null) =>
        GetCompilerDir(version, home);

    /// <summary> Shared runtime directory: ~/.maho/runtime </summary>
    public static string GetRuntimeDir(string? home = null) => Path.Combine(home ?? GetMahoHome(), "runtime");

    /// <summary> Server directory: ~/.maho/server </summary>
    public static string GetServerDir(string? home = null) => Path.Combine(home ?? GetMahoHome(), "server");

    /// <summary> Server manifest path: ~/.maho/server/manifest.json </summary>
    public static string GetServerManifestPath(string? home = null) =>
        Path.Combine(GetServerDir(home), "manifest.json");

    /// <summary> Root/server manifest path: ~/.maho/server/manifest.json (with fallback to ~/.maho/manifest.json) </summary>
    public static string GetRootManifestPath(string? home = null)
    {
        string legacyManifest = Path.Combine(home ?? GetMahoHome(), "manifest.json");
        if (File.Exists(legacyManifest) && !File.Exists(GetServerManifestPath(home)))
            return legacyManifest;
        return GetServerManifestPath(home);
    }

    /// <summary> Bin manifest path: ~/.maho/bin/manifest.json </summary>
    public static string GetBinManifestPath(string? home = null) =>
        Path.Combine(GetBinDir(home), "manifest.json");

    /// <summary> Tool manifest path: ~/.maho/tools/{version}/manifest.json </summary>
    public static string GetToolManifestPath(string version, string? home = null) =>
        Path.Combine(GetToolDir(version, home), "manifest.json");

    /// <summary> Compiler manifest path: ~/.maho/compiler/{version}/manifest.json </summary>
    public static string GetCompilerManifestPath(string version, string? home = null) =>
        Path.Combine(GetCompilerDir(version, home), "manifest.json");

    /// <summary>
    /// Normalizes a version string (e.g., "0.2.0" -> "0.2.0", "v0.2.0" -> "0.2.0", "0.2" -> "0.2.0").
    /// Matches the toolchain directory structure with the -v output version.
    /// </summary>
    public static string NormalizeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "0.2.0";

        version = version.Trim();
        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            version = version[1..].Trim();

        if (Version.TryParse(version, out var parsed))
        {
            int build = parsed.Build >= 0 ? parsed.Build : 0;
            return $"{parsed.Major}.{Math.Max(0, parsed.Minor)}.{build}";
        }

        int firstDot = version.IndexOf('.');
        if (firstDot > 0)
        {
            int secondDot = version.IndexOf('.', firstDot + 1);
            if (secondDot > 0)
                return version;
            return $"{version}.0";
        }

        return $"{version}.0.0";
    }
}
