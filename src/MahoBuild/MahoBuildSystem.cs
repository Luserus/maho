using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Maho.Build;

/// <summary>
/// Represents a loaded project with its configuration, discovered source files, and compilation options.
/// </summary>
public sealed class MahoProject
{
    public string ProjectName { get; }
    public string ProjectDirectory { get; }
    public string ProjectFilePath { get; }
    public MahoProjectConfiguration Configuration { get; }
    public IReadOnlyList<string> SourceFiles { get; }
    public CompilationOptions Options { get; }

    public MahoProject(
        string projectName,
        string projectDirectory,
        string projectFilePath,
        MahoProjectConfiguration configuration,
        IReadOnlyList<string> sourceFiles,
        CompilationOptions options)
    {
        ProjectName = projectName;
        ProjectDirectory = projectDirectory;
        ProjectFilePath = projectFilePath;
        Configuration = configuration;
        SourceFiles = sourceFiles;
        Options = options;
    }
}

/// <summary>
/// Facade build system providing isolated project-file loading, source discovery, and build orchestration.
/// </summary>
public static class MahoBuildSystem
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
    /// Loads a domain-specific <c>.mhpr</c> project file, parses its configuration, discovers source files, and prepares compilation options.
    /// </summary>
    public static MahoProject LoadProject(string projectFilePath, CompilationOptions? options = null)
    {
        options ??= CompilationOptions.Default;
        string fullProjectPath = Path.GetFullPath(projectFilePath);

        if (!string.Equals(Path.GetExtension(fullProjectPath), ".mhpr", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Project files must use the '.mhpr' extension.", nameof(projectFilePath));

        string projectJson = File.ReadAllText(fullProjectPath);
        var config = MahoProjectFileParser.Parse(projectJson);

        string projectDir = Path.GetDirectoryName(fullProjectPath)!;
        string[] sourceFiles = ResolveSourceFiles(projectDir, config);
        string projectName = Path.GetFileNameWithoutExtension(fullProjectPath);

        string? resolvedEntry = config.EntryFile is not null
            ? (Path.IsPathRooted(config.EntryFile) ? config.EntryFile : Path.GetFullPath(Path.Combine(projectDir, config.EntryFile)))
            : options.EntryFile;

        var combinedOptions = options with
        {
            EntryFile = resolvedEntry,
            ImplicitTopLevel = config.ImplicitTopLevel || options.ImplicitTopLevel,
            RootDirectory = projectDir,
            ReferencedProjects = config.ProjectsReferenced,
            GlobalAliases = config.GlobalAliases,
            ProjectFilePath = fullProjectPath
        };

        return new MahoProject(projectName, projectDir, fullProjectPath, config, sourceFiles, combinedOptions);
    }

    /// <summary>
    /// Creates a Compilation instance representing a project file, including its referenced project compilations.
    /// </summary>
    public static Compilation CreateCompilation(string projectFilePath, CompilationOptions? options = null)
    {
        var project = LoadProject(projectFilePath, options);
        var referencedCompilations = new List<Compilation>();
        foreach (var refProj in project.Configuration.ProjectsReferenced)
        {
            string refPath = Path.IsPathRooted(refProj) ? refProj : Path.Combine(project.ProjectDirectory, refProj);
            if (File.Exists(refPath))
                referencedCompilations.Add(CreateCompilation(refPath, options));
        }

        return Compilation.FromFiles(project.SourceFiles, project.ProjectName, project.Options, referencedCompilations);
    }

    /// <summary>
    /// Analyzes a domain-specific <c>.mhpr</c> project file using the build system.
    /// </summary>
    public static CompilerProjectAnalysisResult AnalyzeProject(
        string projectFilePath,
        AnalysisOutput output = AnalysisOutput.None,
        CompilationOptions? options = null)
    {
        var project = LoadProject(projectFilePath, options);
        return MahoCompiler.AnalyzeFiles(project.SourceFiles, output, project.ProjectDirectory, project.Options);
    }

    /// <summary>
    /// Analyzes a domain-specific <c>.mhpr</c> project file using the build system. Alias for <see cref="AnalyzeProject"/>.
    /// </summary>
    public static CompilerProjectAnalysisResult AnalyzeProjectFile(
        string projectFilePath,
        AnalysisOutput output = AnalysisOutput.None,
        CompilationOptions? options = null)
        => AnalyzeProject(projectFilePath, output, options);

    /// <summary>
    /// Compiles a domain-specific <c>.mhpr</c> project file using the build system.
    /// </summary>
    public static CompilerProjectAnalysisResult CompileProject(
        string projectFilePath,
        AnalysisOutput output = AnalysisOutput.None,
        CompilationOptions? options = null)
    {
        var project = LoadProject(projectFilePath, options);
        return MahoCompiler.CompileFiles(project.SourceFiles, output, project.ProjectDirectory, project.Options);
    }

    /// <summary>
    /// Compiles a domain-specific <c>.mhpr</c> project file using the build system. Alias for <see cref="CompileProject"/>.
    /// </summary>
    public static CompilerProjectAnalysisResult CompileProjectFile(
        string projectFilePath,
        AnalysisOutput output = AnalysisOutput.None,
        CompilationOptions? options = null)
        => CompileProject(projectFilePath, output, options);

    /// <summary>
    /// Compiles all source files in a directory using default project directory options.
    /// </summary>
    public static CompilerProjectAnalysisResult CompileDirectory(
        string directoryPath,
        AnalysisOutput output = AnalysisOutput.None,
        CompilationOptions? options = null)
    {
        string fullDirPath = Path.GetFullPath(directoryPath);
        string[] files = ResolveSourceFiles(fullDirPath);
        var dirOptions = (options ?? CompilationOptions.Default) with { RootDirectory = options?.RootDirectory ?? fullDirPath };
        return MahoCompiler.CompileFiles(files, output, fullDirPath, dirOptions);
    }

    /// <summary>
    /// Analyzes all source files in a directory using default project directory options.
    /// </summary>
    public static CompilerProjectAnalysisResult AnalyzeDirectory(
        string directoryPath,
        AnalysisOutput output = AnalysisOutput.None,
        CompilationOptions? options = null)
    {
        string fullDirPath = Path.GetFullPath(directoryPath);
        string[] files = ResolveSourceFiles(fullDirPath);
        var dirOptions = (options ?? CompilationOptions.Default) with { RootDirectory = options?.RootDirectory ?? fullDirPath };
        return MahoCompiler.AnalyzeFiles(files, output, fullDirPath, dirOptions);
    }
}
