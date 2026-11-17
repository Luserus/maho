using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Miryo.Build;

/// <summary>
/// Represents a loaded project with its configuration and discovered source files.
/// </summary>
public sealed class MahoProject
{
    public string ProjectName { get; }
    public string ProjectDirectory { get; }
    public string ProjectFilePath { get; }
    public MahoProjectConfiguration Configuration { get; }
    public IReadOnlyList<string> SourceFiles { get; }
    public string? EntryFile { get; }
    public bool ImplicitTopLevel { get; }
    public bool GlobalUnsafeEnabled { get; }
    public IReadOnlyList<string> ProjectsReferenced { get; }
    public IReadOnlyDictionary<string, string> GlobalAliases { get; }

    public MahoProject(
        string projectName,
        string projectDirectory,
        string projectFilePath,
        MahoProjectConfiguration configuration,
        IReadOnlyList<string> sourceFiles,
        string? entryFile,
        bool implicitTopLevel,
        bool globalUnsafeEnabled,
        IReadOnlyList<string> projectsReferenced,
        IReadOnlyDictionary<string, string> globalAliases)
    {
        ProjectName = projectName;
        ProjectDirectory = projectDirectory;
        ProjectFilePath = projectFilePath;
        Configuration = configuration;
        SourceFiles = sourceFiles;
        EntryFile = entryFile;
        ImplicitTopLevel = implicitTopLevel;
        GlobalUnsafeEnabled = globalUnsafeEnabled;
        ProjectsReferenced = projectsReferenced;
        GlobalAliases = globalAliases;
    }
}

/// <summary>
/// Build system services for project loading, source discovery, and dependency management.
/// Fully decoupled from the compiler library; compilation execution is performed via the CLI compiler.
/// </summary>
public static class MiryoBuildSystem
{
    /// <summary>
    /// Resolves source files for compilation from a file or directory path.
    /// Filters for <c>*.mh</c> files in directories unless instructed otherwise by <paramref name="config"/>.
    /// </summary>
    public static string[] ResolveSourceFiles(string path, MahoProjectConfiguration? config = null)
    {
        if (File.Exists(path))
            return [Path.GetFullPath(path)];

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Input path not found: {path}");

        string projectDir = Path.GetFullPath(path);
        string baseDir = projectDir;

        if (config?.Sources?.Directory is { } configuredDir && !string.IsNullOrWhiteSpace(configuredDir))
        {
            if (configuredDir == "$")
                baseDir = projectDir;
            else if (configuredDir.StartsWith("$/", StringComparison.Ordinal) || configuredDir.StartsWith("$\\", StringComparison.Ordinal))
                baseDir = Path.GetFullPath(Path.Combine(projectDir, configuredDir[2..]));
            else if (Path.IsPathRooted(configuredDir))
                baseDir = Path.GetFullPath(configuredDir);
            else
                baseDir = Path.GetFullPath(Path.Combine(projectDir, configuredDir));

            if (!Directory.Exists(baseDir))
                throw new DirectoryNotFoundException($"Source directory not found: {baseDir}");
        }

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (config?.Sources?.SourceFiles is { Length: > 0 } explicitFiles)
        {
            foreach (string file in explicitFiles)
            {
                string filePath;
                if (file.StartsWith("$/", StringComparison.Ordinal) || file.StartsWith("$\\", StringComparison.Ordinal))
                    filePath = Path.GetFullPath(Path.Combine(projectDir, file[2..]));
                else if (Path.IsPathRooted(file))
                    filePath = Path.GetFullPath(file);
                else
                    filePath = Path.GetFullPath(Path.Combine(baseDir, file));

                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"Configured source file not found: {filePath}", filePath);

                files.Add(filePath);
            }
        }

        if (config?.Sources?.ByName is { } byNamePattern)
        {
            string[] matchingFiles = Directory.GetFiles(baseDir, byNamePattern, SearchOption.AllDirectories);
            foreach (string match in matchingFiles)
                files.Add(Path.GetFullPath(match));
        }
        else if (config?.Sources?.SourceFiles is not { Length: > 0 })
        {
            string[] defaultFiles = Directory.GetFiles(baseDir, "*.mh", SearchOption.AllDirectories);
            foreach (string file in defaultFiles)
                files.Add(Path.GetFullPath(file));
        }

        if (config?.EntryFile is { } configuredEntry && !string.IsNullOrWhiteSpace(configuredEntry))
        {
            string entryPath = Path.IsPathRooted(configuredEntry)
                ? Path.GetFullPath(configuredEntry)
                : Path.GetFullPath(Path.Combine(projectDir, configuredEntry));

            if (!File.Exists(entryPath))
                throw new FileNotFoundException($"Configured EntryFile not found: {entryPath}", entryPath);

            files.Add(entryPath);
        }

        if (files.Count == 0)
            throw new FileNotFoundException($"No source files found in directory: {baseDir}", baseDir);

        string[] sourceFiles = [.. files];
        Array.Sort(sourceFiles, StringComparer.Ordinal);

        return sourceFiles;
    }

    /// <summary>
    /// Searches a directory for a unique <c>.mhpr</c> project file.
    /// Returns the project file path if exactly one is found, or null if none are found.
    /// Throws <see cref="InvalidOperationException"/> if multiple project files are found.
    /// </summary>
    public static string? FindProjectFile(string directoryPath)
    {
        string fullDirPath = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDirPath))
            return null;

        string[] projectFiles = Directory.GetFiles(fullDirPath, "*.mhpr");
        if (projectFiles.Length == 1)
            return projectFiles[0];

        if (projectFiles.Length > 1)
        {
            var fileNames = string.Join(", ", projectFiles.Select(Path.GetFileName));
            throw new InvalidOperationException($"Multiple project files found in '{directoryPath}': {fileNames}. Specify which project file to compile.");
        }

        return null;
    }

    /// <summary>
    /// Searches a directory for a unique <c>.mrws</c> workspace file.
    /// Returns the workspace file path if exactly one is found, or null if none are found.
    /// Throws <see cref="InvalidOperationException"/> if multiple workspace files are found.
    /// </summary>
    public static string? FindWorkspaceFile(string directoryPath)
    {
        string fullDirPath = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDirPath))
            return null;

        string[] wsFiles = Directory.GetFiles(fullDirPath, "*.mrws");
        if (wsFiles.Length == 1)
            return wsFiles[0];

        if (wsFiles.Length > 1)
        {
            var fileNames = string.Join(", ", wsFiles.Select(Path.GetFileName));
            throw new InvalidOperationException($"Multiple workspace files found in '{directoryPath}': {fileNames}. Specify which workspace file to compile.");
        }

        return null;
    }

    /// <summary>
    /// Loads a domain-specific <c>.mrws</c> workspace file, parsing configuration, loading members,
    /// and building the topological dependency graph.
    /// </summary>
    public static MiryoWorkspace LoadWorkspace(string workspaceFilePath, string? customTargetDirectory = null)
    {
        return MiryoWorkspaceLoader.Load(workspaceFilePath, customTargetDirectory);
    }

    /// <summary>
    /// Discovers whether a target path refers to a workspace or project file (directly or within a directory).
    /// </summary>
    public static (string FilePath, bool IsWorkspace)? FindWorkspaceOrProject(string targetPath)
    {
        string fullPath = Path.GetFullPath(targetPath);
        if (File.Exists(fullPath))
        {
            string ext = Path.GetExtension(fullPath);
            if (string.Equals(ext, ".mrws", StringComparison.OrdinalIgnoreCase))
                return (fullPath, true);
            if (string.Equals(ext, ".mhpr", StringComparison.OrdinalIgnoreCase))
                return (fullPath, false);
            return null;
        }

        if (Directory.Exists(fullPath))
        {
            string? wsFile = FindWorkspaceFile(fullPath);
            if (wsFile != null)
                return (wsFile, true);

            string? projFile = FindProjectFile(fullPath);
            if (projFile != null)
                return (projFile, false);
        }

        return null;
    }

    /// <summary>
    /// Loads a domain-specific <c>.mhpr</c> project file, parses its configuration, and discovers source files.
    /// </summary>
    public static MahoProject LoadProject(string projectFilePath)
    {
        string fullProjectPath = Path.GetFullPath(projectFilePath);
        string projectJson = File.ReadAllText(fullProjectPath);
        var config = MahoProjectFileParser.Parse(projectJson);

        string projectDir = Path.GetDirectoryName(fullProjectPath)!;
        string[] sourceFiles = ResolveSourceFiles(projectDir, config);
        string projectName = Path.GetFileNameWithoutExtension(fullProjectPath);

        string? resolvedEntry = config.EntryFile is not null
            ? (Path.IsPathRooted(config.EntryFile) ? config.EntryFile : Path.GetFullPath(Path.Combine(projectDir, config.EntryFile)))
            : null;

        return new MahoProject(
            projectName,
            projectDir,
            fullProjectPath,
            config,
            sourceFiles,
            resolvedEntry,
            config.ImplicitTopLevel,
            config.GlobalUnsafeEnabled,
            config.ProjectsReferenced,
            config.GlobalAliases);
    }
}
