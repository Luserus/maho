using System;
using System.Collections.Generic;
using System.Reflection;
using Maho.Text;

namespace Maho;

/// <summary>
/// Unified compiler entrypoint and orchestration engine for Maho.
/// Supports single source compilation, multi-file batch compilation, domain-specific project files (.mhpr),
/// interactive REPL sessions, and compilation querying.
/// </summary>
public static class MahoCompiler
{
    /// <summary>
    /// The version of the Maho compiler library.
    /// </summary>
    public static Version Version => typeof(MahoCompiler).Assembly.GetName().Version ?? new Version(0, 2, 0);

    /// <summary>
    /// Human-readable version string for the Maho compiler library.
    /// </summary>
    public static string VersionString
    {
        get
        {
            var informational = typeof(MahoCompiler).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                int plusIndex = informational.IndexOf('+');

                return plusIndex >= 0 ? informational[..plusIndex] : informational;
            }

            return Version.ToString(3);
        }
    }

    /// <summary>
    /// Spawns an interactive <see cref="AnalysisSession"/> rooted in an optional base compilation.
    /// </summary>
    public static AnalysisSession CreateSession(Compilation? rootCompilation = null, CompilationOptions? options = null) =>
        new AnalysisSession(rootCompilation, options);

    /// <summary>
    /// Compiles an in-memory source string and returns a <see cref="Compilation"/>.
    /// </summary>
    /// <param name="source">The in-memory Maho source code.</param>
    /// <param name="filePath">The virtual or physical source file path.</param>
    /// <param name="options">Optional compilation configuration options.</param>
    /// <param name="cachedCompilation">Optional previously resolved compilation to reuse unchanged AST and semantics.</param>
    /// <returns>An immutable <see cref="Compilation"/> instance.</returns>
    public static Compilation CompileSource(string source, string filePath = "source.mh", CompilationOptions? options = null, Compilation? cachedCompilation = null) =>
        Compilation.FromSource(source, filePath, options, cachedCompilation: cachedCompilation);

    /// <summary>
    /// Analyzes a collection of source files and returns an immutable <see cref="Compilation"/>.
    /// </summary>
    /// <param name="filePaths">The collection of source file paths on disk.</param>
    /// <param name="rootPath">Optional root workspace directory path.</param>
    /// <param name="options">Optional compilation configuration options.</param>
    /// <param name="projectName">Optional explicit project or assembly name.</param>
    /// <param name="referencedCompilations">Optional external referenced project compilations.</param>
    /// <param name="cachedCompilation">Optional previously resolved compilation to reuse unchanged AST trees and semantics.</param>
    /// <returns>An immutable <see cref="Compilation"/> instance.</returns>
    public static Compilation AnalyzeFiles(
        IEnumerable<string> filePaths,
        string? rootPath = null,
        CompilationOptions? options = null,
        string? projectName = null,
        IReadOnlyList<Compilation>? referencedCompilations = null,
        Compilation? cachedCompilation = null)
    {
        options ??= CompilationOptions.Default;

        if (rootPath is not null && options.RootDirectory is null)
            options = options with { RootDirectory = rootPath };

        return Compilation.FromFiles(filePaths, projectName, options, referencedCompilations, cachedCompilation);
    }

    /// <summary>
    /// Compiler API entry point that constructs an immutable <see cref="Compilation"/>.
    /// </summary>
    /// <param name="filePaths">The collection of source file paths on disk.</param>
    /// <param name="projectName">Optional explicit project or assembly name.</param>
    /// <param name="options">Optional compilation configuration options.</param>
    /// <param name="referencedCompilations">Optional external referenced project compilations.</param>
    /// <param name="cachedCompilation">Optional previously resolved compilation to reuse unchanged AST trees and semantics.</param>
    /// <returns>An immutable <see cref="Compilation"/> instance.</returns>
    public static Compilation CreateCompilation(
        IEnumerable<string> filePaths,
        string? projectName = null,
        CompilationOptions? options = null,
        IReadOnlyList<Compilation>? referencedCompilations = null,
        Compilation? cachedCompilation = null) =>
        Compilation.FromFiles(filePaths, projectName, options, referencedCompilations, cachedCompilation);

    /// <summary>
    /// Compiles a collection of source files and returns an immutable <see cref="Compilation"/>.
    /// </summary>
    /// <param name="filePaths">The collection of source file paths on disk.</param>
    /// <param name="rootPath">Optional root workspace directory path.</param>
    /// <param name="options">Optional compilation configuration options.</param>
    /// <param name="projectName">Optional explicit project or assembly name.</param>
    /// <param name="referencedCompilations">Optional external referenced project compilations.</param>
    /// <param name="cachedCompilation">Optional previously resolved compilation to reuse unchanged AST trees and semantics.</param>
    /// <returns>An immutable <see cref="Compilation"/> instance.</returns>
    public static Compilation CompileFiles(
        IEnumerable<string> filePaths,
        string? rootPath = null,
        CompilationOptions? options = null,
        string? projectName = null,
        IReadOnlyList<Compilation>? referencedCompilations = null,
        Compilation? cachedCompilation = null)
    {
        var compilation = AnalyzeFiles(filePaths, rootPath, options, projectName, referencedCompilations, cachedCompilation);

        if (!compilation.HasErrors)
            throw new CompilerPipelineNotImplementedException("The lowering and code-generation pipeline has not been implemented.", compilation);

        return compilation;
    }

    internal static TextSpanInfo CreateSpanInfo(TextSpan span, SourceText text) =>
        new TextSpanInfo(span.Start, span.Length, span.End, new TextLocation(span.GetStartLine(text) + 1, span.GetStartColumn(text) + 1),
            new TextLocation(span.GetEndLine(text) + 1, span.GetEndColumn(text) + 1));
}
