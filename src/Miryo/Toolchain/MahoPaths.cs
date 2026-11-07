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
    /// Supports MAHO_HOME override and co-located development/bin directories.
    /// </summary>
    public static string GetMahoHome()
    {
        string? env = Environment.GetEnvironmentVariable("MAHO_HOME");
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);

        string baseDir = AppContext.BaseDirectory;
        string parentDir = Path.GetDirectoryName(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? baseDir;

        // Check if running from bin/miryo with sibling bin/maho
        string siblingMaho = Path.Combine(parentDir, "maho");
        if (Directory.Exists(siblingMaho) && Directory.Exists(Path.Combine(siblingMaho, "tools")))
            return Path.GetFullPath(siblingMaho);

        // Check if running inside maho layout directly
        if (Directory.Exists(Path.Combine(baseDir, "tools")) &&
            (Directory.Exists(Path.Combine(baseDir, "compiler")) ||
             Directory.Exists(Path.Combine(baseDir, "runtime")) ||
             Directory.Exists(Path.Combine(baseDir, "server"))))
            return Path.GetFullPath(baseDir);

        if (Directory.Exists(Path.Combine(parentDir, "tools")) &&
            (Directory.Exists(Path.Combine(parentDir, "compiler")) ||
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

    /// <summary> Server manager executable path: ~/.maho/server/server-manager </summary>
    public static string? FindServerManager(string? home = null)
    {
        string serverDir = GetServerDir(home);
        string exeName = OperatingSystem.IsWindows() ? "server-manager.exe" : "server-manager";
        string exePath = Path.Combine(serverDir, exeName);

        if (File.Exists(exePath))
            return exePath;

        string dllPath = Path.Combine(serverDir, "server-manager.dll");

        if (File.Exists(dllPath))
            return dllPath;

        return null;
    }

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
    /// Normalizes a version string to major.minor format (e.g., "0.2.0" -> "0.2", "v0.2" -> "0.2", "1.0.0" -> "1.0").
    /// Matches the toolchain directory structure where patches directly overwrite files within the major.minor layout.
    /// </summary>
    public static string NormalizeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "0.2";

        version = version.Trim();
        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            version = version[1..].Trim();

        if (Version.TryParse(version, out var parsed))
        {
            return $"{parsed.Major}.{Math.Max(0, parsed.Minor)}";
        }

        int firstDot = version.IndexOf('.');
        if (firstDot > 0)
        {
            int secondDot = version.IndexOf('.', firstDot + 1);
            if (secondDot > 0)
                return version[..secondDot];
            return version;
        }

        return $"{version}.0";
    }
}
