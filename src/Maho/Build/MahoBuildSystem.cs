using System;
using System.Collections.Generic;
using System.IO;
using Maho.Analysis;

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
    /// Filters for <c>*.mh</c> files in directories unless instructed otherwise.
    /// </summary>
    public static string[] ResolveSourceFiles(string path, MahoProjectConfiguration? config = null)
    {
        if (File.Exists(path))
            return [Path.GetFullPath(path)];

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Input path not found: {path}");

        string[] sourceFiles = Directory.GetFiles(path, "*.mh", SearchOption.AllDirectories);
        Array.Sort(sourceFiles, StringComparer.Ordinal);

        if (sourceFiles.Length == 0)
            throw new FileNotFoundException($"No source files found in directory: {path}", path);

        return sourceFiles;
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

        string? resolvedEntry = config.EntryFile != null
            ? (Path.IsPathRooted(config.EntryFile) ? config.EntryFile : Path.GetFullPath(Path.Combine(projectDir, config.EntryFile)))
            : options.EntryFile;

        var combinedOptions = options with
        {
            EntryFile = resolvedEntry,
            ImplicitTopLevel = config.ImplicitTopLevel || options.ImplicitTopLevel,
            RootDirectory = projectDir,
            ReferencedProjects = config.ProjectsReferenced,
            GlobalAliases = config.GlobalAliases
        };

        return new MahoProject(projectName, projectDir, fullProjectPath, config, sourceFiles, combinedOptions);
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
