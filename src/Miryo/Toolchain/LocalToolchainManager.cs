using System;
using System.Collections.Generic;
using System.IO;

namespace Miryo.Toolchain;

/// <summary>
/// Manages the local ~/.maho toolchain structure and shims.
/// </summary>
public sealed class LocalToolchainManager : IToolchainManager
{
    public string MahoHome { get; }

    public LocalToolchainManager(string? mahoHome = null)
    {
        MahoHome = mahoHome ?? MahoPaths.GetMahoHome();
    }

    public string ActiveVersion
    {
        get
        {
            string rootManifestPath = MahoPaths.GetRootManifestPath(MahoHome);
            if (File.Exists(rootManifestPath))
            {
                var manifest = ToolchainRootManifest.Load(rootManifestPath);
                if (!string.IsNullOrWhiteSpace(manifest.ActiveCompilerVersion))
                    return MahoPaths.NormalizeVersion(manifest.ActiveCompilerVersion);
            }

            // Fall back to scanning tools/ directory for the highest version
            var installed = InstalledVersions;
            if (installed.Count > 0)
                return installed[^1];

            return "0.2";
        }
    }

    public IReadOnlyList<string> InstalledVersions
    {
        get
        {
            string toolsDir = MahoPaths.GetToolsDir(MahoHome);
            if (!Directory.Exists(toolsDir))
                return ["0.2"];

            var list = new List<string>();
            foreach (string dir in Directory.GetDirectories(toolsDir))
            {
                string name = Path.GetFileName(dir);
                if (char.IsDigit(name[0]))
                    list.Add(MahoPaths.NormalizeVersion(name));
            }

            list.Sort(StringComparer.Ordinal);
            return list.Count > 0 ? list : ["0.2"];
        }
    }

    public string? FindTool(string toolName, string? version = null)
    {
        string normVersion = MahoPaths.NormalizeVersion(version ?? ActiveVersion);
        string toolDir = MahoPaths.GetToolDir(normVersion, MahoHome);
        string exeExt = OperatingSystem.IsWindows() ? ".exe" : "";

        // 1. Direct tool directory: ~/.maho/tools/{version}/{toolName}
        string toolPath = Path.Combine(toolDir, toolName + exeExt);
        if (File.Exists(toolPath))
            return toolPath;

        string dllPath = Path.Combine(toolDir, toolName + ".dll");
        if (File.Exists(dllPath))
            return dllPath;

        // 2. Shims directory: ~/.maho/shims/{toolName}
        string shimPath = Path.Combine(MahoPaths.GetShimsDir(MahoHome), toolName + exeExt);
        if (File.Exists(shimPath))
            return shimPath;

        // 3. Fallback to co-located binaries in application directory (for development)
        string baseDir = AppContext.BaseDirectory;
        string colocated = Path.Combine(baseDir, toolName + exeExt);
        if (File.Exists(colocated))
            return colocated;

        string colocatedDll = Path.Combine(baseDir, toolName + ".dll");
        if (File.Exists(colocatedDll))
            return colocatedDll;

        return null;
    }

    public void UpdateShims(string? version = null)
    {
        string targetVersion = MahoPaths.NormalizeVersion(version ?? ActiveVersion);
        string shimsDir = MahoPaths.GetShimsDir(MahoHome);
        Directory.CreateDirectory(shimsDir);

        string[] standardTools = ["mahoc", "maho-server", "maho-ls"];
        string toolDir = MahoPaths.GetToolDir(targetVersion, MahoHome);

        foreach (string tool in standardTools)
        {
            string targetBinary = Path.Combine(toolDir, OperatingSystem.IsWindows() ? tool + ".exe" : tool);

            if (OperatingSystem.IsWindows())
            {
                string cmdPath = Path.Combine(shimsDir, tool + ".cmd");
                string content = $"@\"{targetBinary}\" %*\n";
                File.WriteAllText(cmdPath, content);
            }
            else
            {
                string shimPath = Path.Combine(shimsDir, tool);
                try
                {
                    if (File.Exists(shimPath))
                        File.Delete(shimPath);

                    // Try creating symbolic link
                    File.CreateSymbolicLink(shimPath, targetBinary);
                }
                catch
                {
                    // Fall back to shell script launcher if symlinks are not supported
                    string script = $"#!/usr/bin/env sh\nexec \"{targetBinary}\" \"$@\"\n";
                    File.WriteAllText(shimPath, script);
                    try
                    {
                        File.SetUnixFileMode(shimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                                      UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                                      UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}
