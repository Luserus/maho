using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Miryo.Build;

/// <summary>
/// Discovers, recursively resolves, and loads Miryo workspace hierarchies.
/// </summary>
public static class MiryoWorkspaceLoader
{
    /// <summary>
    /// Loads a Miryo workspace from disk, resolving member projects and nested sub-workspaces.
    /// </summary>
    /// <param name="workspaceFilePath">Path to the <c>.mrws</c> workspace file.</param>
    /// <param name="customTargetDirectory">Optional override for the workspace target directory.</param>
    /// <returns>A fully resolved <see cref="MiryoWorkspace"/> instance.</returns>
    public static MiryoWorkspace Load(string workspaceFilePath, string? customTargetDirectory = null)
    {
        string fullPath = Path.GetFullPath(workspaceFilePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Workspace file not found: {fullPath}", fullPath);

        var callStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return LoadInternal(fullPath, callStack, parentShared: null, customTargetDirectory: customTargetDirectory);
    }

    private static MiryoWorkspace LoadInternal(
        string workspaceFilePath,
        HashSet<string> callStack,
        MiryoWorkspaceSharedConfiguration? parentShared,
        string? customTargetDirectory)
    {
        string canonicalPath = Path.GetFullPath(workspaceFilePath);
        if (!callStack.Add(canonicalPath))
        {
            var chain = callStack.Concat([canonicalPath]).Select(Path.GetFileName);
            throw new InvalidOperationException($"Cyclic workspace reference detected: {string.Join(" -> ", chain)}");
        }

        string workspaceDir = Path.GetDirectoryName(canonicalPath)!;
        string configText = File.ReadAllText(canonicalPath);
        var config = MahoProjectFileParser.ParseWorkspace(configText);

        string workspaceName = config.Name
            ?? Path.GetFileNameWithoutExtension(canonicalPath);

        // Effective shared config = parent shared merged with current workspace shared
        var effectiveShared = MergeSharedConfigs(parentShared, config.Shared);

        var childWorkspaces = new List<MiryoWorkspace>();
        var projects = new Dictionary<string, MahoProject>(StringComparer.OrdinalIgnoreCase);

        var candidatePaths = ResolveMemberPaths(workspaceDir, config.Members, config.Exclude);

        foreach (string path in candidatePaths)
        {
            if (File.Exists(path))
            {
                string ext = Path.GetExtension(path);
                if (string.Equals(ext, ".mrws", StringComparison.OrdinalIgnoreCase))
                {
                    var childWs = LoadInternal(path, callStack, effectiveShared, customTargetDirectory);
                    childWorkspaces.Add(childWs);

                    foreach (var childProj in childWs.Projects)
                        projects[Path.GetFullPath(childProj.ProjectFilePath)] = childProj;
                }
                else if (string.Equals(ext, ".mhpr", StringComparison.OrdinalIgnoreCase))
                {
                    var proj = LoadAndApplyShared(path, effectiveShared);
                    projects[Path.GetFullPath(proj.ProjectFilePath)] = proj;
                }
            }
            else if (Directory.Exists(path))
            {
                string? wsFile = MiryoBuildSystem.FindWorkspaceFile(path);
                if (wsFile != null)
                {
                    var childWs = LoadInternal(wsFile, callStack, effectiveShared, customTargetDirectory);
                    childWorkspaces.Add(childWs);

                    foreach (var childProj in childWs.Projects)
                        projects[Path.GetFullPath(childProj.ProjectFilePath)] = childProj;
                }
                else
                {
                    string? projFile = MiryoBuildSystem.FindProjectFile(path);
                    if (projFile != null)
                    {
                        var proj = LoadAndApplyShared(projFile, effectiveShared);
                        projects[Path.GetFullPath(proj.ProjectFilePath)] = proj;
                    }
                    else
                    {
                        throw new FileNotFoundException($"No '.mrws' or '.mhpr' file found in workspace member directory: {path}", path);
                    }
                }
            }
            else
            {
                throw new FileNotFoundException($"Workspace member not found: {path}", path);
            }
        }

        callStack.Remove(canonicalPath);

        var projectList = projects.Values.ToList();
        return new MiryoWorkspace(
            workspaceName,
            workspaceDir,
            canonicalPath,
            config,
            projectList,
            childWorkspaces,
            customTargetDirectory);
    }

    private static MahoProject LoadAndApplyShared(string projectFilePath, MiryoWorkspaceSharedConfiguration? shared)
    {
        var project = MiryoBuildSystem.LoadProject(projectFilePath);

        if (shared == null)
            return project;

        // Apply shared aliases (project-level aliases take precedence)
        var mergedAliases = new Dictionary<string, string>(shared.GlobalAliases, StringComparer.Ordinal);
        foreach (var (k, v) in project.GlobalAliases)
            mergedAliases[k] = v;

        bool effectiveUnsafe = project.GlobalUnsafeEnabled || (shared.GlobalUnsafeEnabled == true);

        return new MahoProject(
            project.ProjectName,
            project.ProjectDirectory,
            project.ProjectFilePath,
            project.Configuration,
            project.SourceFiles,
            project.EntryFile,
            project.ImplicitTopLevel,
            effectiveUnsafe,
            project.ProjectsReferenced,
            mergedAliases);
    }

    private static MiryoWorkspaceSharedConfiguration? MergeSharedConfigs(
        MiryoWorkspaceSharedConfiguration? parent,
        MiryoWorkspaceSharedConfiguration? current)
    {
        if (parent == null && current == null)
            return null;

        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parent != null)
        {
            foreach (var (k, v) in parent.GlobalAliases)
                aliases[k] = v;
        }
        if (current != null)
        {
            foreach (var (k, v) in current.GlobalAliases)
                aliases[k] = v;
        }

        bool? unsafeEnabled = current?.GlobalUnsafeEnabled ?? parent?.GlobalUnsafeEnabled;

        return new MiryoWorkspaceSharedConfiguration
        {
            GlobalAliases = aliases,
            GlobalUnsafeEnabled = unsafeEnabled
        };
    }

    private static List<string> ResolveMemberPaths(string baseDir, string[] memberPatterns, string[] excludePatterns)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string rawPattern in memberPatterns)
        {
            string pattern = rawPattern.Trim();
            if (string.IsNullOrEmpty(pattern))
                continue;

            // Handle globbing if pattern contains wildcards
            if (pattern.Contains('*') || pattern.Contains('?'))
            {
                var matches = ExpandGlob(baseDir, pattern);
                foreach (string m in matches)
                    results.Add(Path.GetFullPath(m));
            }
            else
            {
                string resolved = Path.IsPathRooted(pattern)
                    ? Path.GetFullPath(pattern)
                    : Path.GetFullPath(Path.Combine(baseDir, pattern));
                results.Add(resolved);
            }
        }

        // Apply exclusions
        if (excludePatterns.Length > 0)
        {
            var toRemove = new List<string>();
            foreach (string path in results)
            {
                if (IsExcluded(path, baseDir, excludePatterns))
                    toRemove.Add(path);
            }
            foreach (string rem in toRemove)
                results.Remove(rem);
        }

        return results.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ExpandGlob(string baseDir, string pattern)
    {
        string normalized = pattern.Replace('\\', '/');
        int firstWildcard = normalized.IndexOfAny(['*', '?']);
        string fixedPrefix = normalized[..firstWildcard];
        int lastSlash = fixedPrefix.LastIndexOf('/');

        string searchRoot;
        string subPattern;

        if (lastSlash >= 0)
        {
            string rootPart = fixedPrefix[..lastSlash];
            searchRoot = Path.IsPathRooted(rootPart) ? Path.GetFullPath(rootPart) : Path.GetFullPath(Path.Combine(baseDir, rootPart));
            subPattern = normalized[(lastSlash + 1)..];
        }
        else
        {
            searchRoot = baseDir;
            subPattern = normalized;
        }

        if (!Directory.Exists(searchRoot))
            return [];

        bool recursive = subPattern.Contains("**/");
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        string fileSearchPattern = subPattern.Replace("**/", "");
        if (fileSearchPattern.Contains('/'))
            fileSearchPattern = Path.GetFileName(fileSearchPattern);

        if (string.IsNullOrWhiteSpace(fileSearchPattern))
            fileSearchPattern = "*";

        var matches = new List<string>();

        // Check matching directories
        foreach (string dir in Directory.GetDirectories(searchRoot, "*", searchOption))
        {
            string rel = Path.GetRelativePath(baseDir, dir).Replace('\\', '/');
            if (WildcardMatch(rel, normalized) || WildcardMatch(Path.GetFileName(dir), fileSearchPattern))
            {
                matches.Add(dir);
            }
        }

        // Check matching files (.mrws and .mhpr)
        foreach (string file in Directory.GetFiles(searchRoot, "*", searchOption))
        {
            string ext = Path.GetExtension(file);
            if (string.Equals(ext, ".mhpr", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".mrws", StringComparison.OrdinalIgnoreCase))
            {
                string rel = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
                if (WildcardMatch(rel, normalized) || WildcardMatch(Path.GetFileName(file), fileSearchPattern))
                {
                    matches.Add(file);
                }
            }
        }

        return matches;
    }

    private static bool IsExcluded(string path, string baseDir, string[] excludePatterns)
    {
        string relPath = Path.GetRelativePath(baseDir, path).Replace('\\', '/');
        string fileName = Path.GetFileName(path);

        foreach (string rawEx in excludePatterns)
        {
            string ex = rawEx.Trim().Replace('\\', '/').TrimEnd('/');
            if (WildcardMatch(relPath, ex) || WildcardMatch(fileName, ex) || relPath.StartsWith(ex + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool WildcardMatch(string text, string pattern)
    {
        if (pattern == "*" || pattern == "**")
            return true;

        return SimpleWildcardMatch(text, pattern, 0, 0);
    }

    private static bool SimpleWildcardMatch(string text, string pattern, int tIdx, int pIdx)
    {
        while (pIdx < pattern.Length && tIdx < text.Length)
        {
            if (pattern[pIdx] == '*')
            {
                while (pIdx < pattern.Length && pattern[pIdx] == '*')
                    pIdx++;

                if (pIdx == pattern.Length)
                    return true;

                for (int i = tIdx; i < text.Length; i++)
                {
                    if (SimpleWildcardMatch(text, pattern, i, pIdx))
                        return true;
                }

                return false;
            }

            if (pattern[pIdx] == '?' || char.ToLowerInvariant(pattern[pIdx]) == char.ToLowerInvariant(text[tIdx]))
            {
                pIdx++;
                tIdx++;
            }
            else
            {
                return false;
            }
        }

        while (pIdx < pattern.Length && pattern[pIdx] == '*')
            pIdx++;

        return pIdx == pattern.Length && tIdx == text.Length;
    }
}

