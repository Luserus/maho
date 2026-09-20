using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Maho.Diagnostics;
using Maho.Resolution;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Analysis;

/// <summary>
/// Unified compiler entrypoint and orchestration engine for Maho.
/// Supports single source compilation, multi-file batch compilation, domain-specific project files (.mhpr),
/// interactive REPL sessions, and polymorphic output generation (diagnostics, debug AST/tokens, and IL).
/// </summary>
public static class MahoCompiler
{
    /// <summary>
    /// The version of the Maho compiler library.
    /// </summary>
    public static Version Version =>
        typeof(MahoCompiler).Assembly.GetName().Version ?? new Version(0, 1, 0);

    /// <summary>
    /// Human-readable version string for the Maho compiler library.
    /// </summary>
    public static string VersionString
    {
        get
        {
            var informational = typeof(MahoCompiler).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

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
        new(rootCompilation, options);

    /// <summary>
    /// Compiles an in-memory source string and returns a polymorphic <see cref="DebugCompilationOutput"/>.
    /// </summary>
    public static DebugCompilationOutput CompileSource(
        string source,
        AnalysisOutput output = AnalysisOutput.None,
        string filePath = "source.mh",
        CompilationOptions? options = null)
    {
        options ??= CompilationOptions.Default;
        var text = new SourceText(source);
        var dm = new DiagnosticsManager(text);

        var lexer = new Lexer(text, dm);
        lexer.Lex();

        var parser = new Parser(text, dm, options.ImplicitTopLevel);
        var root = parser.Parse(lexer.Tokens);

        var syntaxTree = SyntaxTree.CreateSingleRoot(root, filePath);
        var resolver = new Resolver();
        resolver.Resolve(syntaxTree);

        string? lexerJson = output.HasFlag(AnalysisOutput.Lexer) ? lexer.ToString() : null;
        string? parserJson = output.HasFlag(AnalysisOutput.Parser) ? parser.ToString() : null;

        var projectedDiagnostics = ProjectDiagnostics(dm.Diagnostics, text, filePath);

        return new DebugCompilationOutput(
            filePath,
            lexerJson,
            parserJson,
            projectedDiagnostics);
    }

    /// <summary> Backward-compatible alias for <see cref="CompileSource"/>. </summary>
    public static DebugCompilationOutput AnalyzeText(string text, AnalysisOutput output = AnalysisOutput.None, string filePath = "source.mh") =>
        CompileSource(text, output, filePath);

    /// <summary> Backward-compatible alias for single-file analysis. </summary>
    public static DebugCompilationOutput AnalyzeFile(string filePath, AnalysisOutput output = AnalysisOutput.None)
    {
        string fullPath = Path.GetFullPath(filePath);
        string content = File.ReadAllText(fullPath);

        return CompileSource(content, output, fullPath);
    }

    private static CompilerProjectAnalysisResult CompileFilesCore(
        IEnumerable<string> filePaths,
        AnalysisOutput output = AnalysisOutput.None,
        string? rootPath = null,
        CompilationOptions? options = null)
    {
        options ??= CompilationOptions.Default;
        if (rootPath is not null && options.RootDirectory is null)
            options = options with { RootDirectory = rootPath };
        var pathsList = filePaths.Select(Path.GetFullPath).ToList();
        string projectName = rootPath is not null ? Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : "Project";

        if (pathsList.Count == 0)
            return new CompilerProjectAnalysisResult(projectName, []);

        var fileResults = new CompilerBatchFileResult?[pathsList.Count];
        var sourceTexts = new SourceText?[pathsList.Count];
        var roots = new CompilationUnit?[pathsList.Count];
        var fileDms = new DiagnosticsManager?[pathsList.Count];
        var fileLexers = new Lexer?[pathsList.Count];
        var fileParsers = new Parser?[pathsList.Count];

        Parallel.For(0, pathsList.Count, i =>
        {
            string path = pathsList[i];
            try
            {
                var text = new SourceText(new SourceFile(path));
                sourceTexts[i] = text;

                bool isEntryFile = options.EntryFile != null &&
                    (string.Equals(path, options.EntryFile, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(Path.GetFileName(path), options.EntryFile, StringComparison.OrdinalIgnoreCase));

                bool isSingleFileScript = pathsList.Count == 1 && options.RootDirectory == null;
                bool allowImplicit = options.ImplicitTopLevel || (isSingleFileScript && (options.EntryFile == null || isEntryFile));

                var dm = new DiagnosticsManager(text);
                fileDms[i] = dm;

                var lexer = new Lexer(text, dm);
                lexer.Lex();
                fileLexers[i] = lexer;

                var parser = new Parser(text, dm, allowImplicit);
                var root = parser.Parse(lexer.Tokens);
                fileParsers[i] = parser;
                roots[i] = root;
            }
            catch (Exception ex)
            {
                fileResults[i] = new CompilerBatchFileResult(path, null, FormatFileError(path, ex), isInternalError: !IsUserFacingError(ex));
            }
        });

        var validPaths = new List<string>(pathsList.Count);
        var validSourceTexts = new List<SourceText>(pathsList.Count);
        var validRoots = new List<CompilationUnit>(pathsList.Count);
        var validFileDms = new List<DiagnosticsManager>(pathsList.Count);
        var validFileLexers = new List<Lexer>(pathsList.Count);
        var validFileParsers = new List<Parser>(pathsList.Count);

        for (int i = 0; i < pathsList.Count; i++)
        {
            if (fileResults[i] is null)
            {
                validPaths.Add(pathsList[i]);
                validSourceTexts.Add(sourceTexts[i]!);
                validRoots.Add(roots[i]!);
                validFileDms.Add(fileDms[i]!);
                validFileLexers.Add(fileLexers[i]!);
                validFileParsers.Add(fileParsers[i]!);
            }
        }

        // Validate top-level statements across files
        List<(int Index, CompilationUnit Root, DiagnosticsManager Dm)> filesWithTopLevel = [];
        for (int i = 0; i < validRoots.Count; i++)
        {
            if (validRoots[i].EnablesTopLevelStatements && ContainsTopLevelStatement(validRoots[i].Members))
                filesWithTopLevel.Add((i, validRoots[i], validFileDms[i]));
        }

        string? effectiveEntryFile = options.EntryFile;
        if (filesWithTopLevel.Count == 1 && effectiveEntryFile == null)
            effectiveEntryFile = validPaths[filesWithTopLevel[0].Index];
        else if (pathsList.Count == 1 && options.RootDirectory == null && effectiveEntryFile == null)
            effectiveEntryFile = pathsList[0];

        if (filesWithTopLevel.Count > 1)
        {
            foreach (var (_, candRoot, candDm) in filesWithTopLevel)
            {
                candDm.ReportError("MH0012", "Only one source file may contain top-level statements.", GetTopLevelPragmaSpan(candRoot));
            }
        }

        // Project-wide resolution
        var syntaxTree = new SyntaxTree(projectName, [.. validRoots]);
        var resolver = new Resolver();
        var context = resolver.Resolve(syntaxTree);

        var finalResults = new List<CompilerBatchFileResult>(pathsList.Count);
        int validIdx = 0;
        for (int i = 0; i < pathsList.Count; i++)
        {
            if (fileResults[i] is { } failedResult)
            {
                finalResults.Add(failedResult);
                continue;
            }

            string path = validPaths[validIdx];
            var text = validSourceTexts[validIdx];
            var dm = validFileDms[validIdx];
            var lexer = validFileLexers[validIdx];
            var parser = validFileParsers[validIdx];
            validIdx++;

            string? lexerJson = output.HasFlag(AnalysisOutput.Lexer) ? lexer.ToString() : null;
            string? parserJson = output.HasFlag(AnalysisOutput.Parser) ? parser.ToString() : null;

            var projectedDiagnostics = ProjectDiagnostics(dm.Diagnostics, text, path);

            var debugOutput = new DebugCompilationOutput(
                path,
                lexerJson,
                parserJson,
                projectedDiagnostics);

            finalResults.Add(new CompilerBatchFileResult(path, debugOutput));
        }

        var compilation = Compilation.FromFiles(pathsList, projectName, options with
        {
            EntryFile = effectiveEntryFile,
            ImplicitTopLevel = options.ImplicitTopLevel || (pathsList.Count == 1 && options.RootDirectory == null)
        });

        return new CompilerProjectAnalysisResult(projectName, finalResults.ToArray())
        {
            EntryFile = effectiveEntryFile,
            Compilation = compilation
        };
    }

    /// <summary>
    /// Analyzes a collection of source files and returns a structured <see cref="CompilerProjectAnalysisResult"/>.
    /// </summary>
    public static CompilerProjectAnalysisResult AnalyzeFiles(
        IEnumerable<string> filePaths,
        AnalysisOutput output = AnalysisOutput.None,
        string? rootPath = null,
        CompilationOptions? options = null)
    {
        return CompileFilesCore(filePaths, output, rootPath, options);
    }

    /// <summary>
    /// Compiles a collection of source files and reaches the lowering/codegen stage.
    /// Throws <see cref="CompilerPipelineNotImplementedException"/> after successful front-end analysis.
    /// </summary>
    public static CompilerProjectAnalysisResult CompileFiles(
        IEnumerable<string> filePaths,
        AnalysisOutput output = AnalysisOutput.None,
        string? rootPath = null,
        CompilationOptions? options = null)
    {
        var analysis = AnalyzeFiles(filePaths, output, rootPath, options);
        if (!analysis.HasErrors)
            throw new CompilerPipelineNotImplementedException("The lowering and code-generation pipeline has not been implemented.", analysis);
        return analysis;
    }

    /// <summary>
    /// Analyzes a domain-specific <c>.mhpr</c> project file.
    /// </summary>
    public static CompilerProjectAnalysisResult AnalyzeProjectFile(
        string projectFilePath,
        AnalysisOutput output = AnalysisOutput.None,
        CompilationOptions? options = null)
    {
        return Maho.Build.MahoBuildSystem.AnalyzeProject(projectFilePath, output, options);
    }

    /// <summary>
    /// Compiles a domain-specific <c>.mhpr</c> project file and reaches the lowering/codegen stage.
    /// Throws <see cref="CompilerPipelineNotImplementedException"/> after successful front-end analysis.
    /// </summary>
    public static CompilerProjectAnalysisResult CompileProjectFile(
        string projectFilePath,
        AnalysisOutput output = AnalysisOutput.None,
        CompilationOptions? options = null)
    {
        return Maho.Build.MahoBuildSystem.CompileProject(projectFilePath, output, options);
    }

    internal static TextSpanInfo CreateSpanInfo(TextSpan span, SourceText text) =>
        new(
            span.Start,
            span.Length,
            span.End,
            new TextLocation(span.GetStartLine(text) + 1, span.GetStartColumn(text) + 1),
            new TextLocation(span.GetEndLine(text) + 1, span.GetEndColumn(text) + 1));

    private static List<DiagnosticInfo> ProjectDiagnostics(
        IReadOnlyList<Diagnostic> diagnostics,
        SourceText sourceText,
        string filePath)
    {
        var result = new List<DiagnosticInfo>(diagnostics.Count);
        foreach (var diag in diagnostics)
            result.Add(DiagnosticInfo.FromDiagnostic(diag, sourceText, filePath));
        return result;
    }

    private static bool ContainsTopLevelStatement(IReadOnlyList<TopLevel> members)
    {
        foreach (TopLevel member in members)
        {
            switch (member)
            {
                case TopLevelStatement:
                    return true;
                case TopLevelBlockDeclaration block when ContainsTopLevelStatement(block.Members):
                    return true;
                case NamespaceDeclaration { Body: NamespaceBlockBody body } when ContainsTopLevelStatement(body.Members):
                    return true;
            }
        }

        return false;
    }

    private static TextSpan GetTopLevelPragmaSpan(CompilationUnit unit)
    {
        foreach (PragmaDirective pragma in unit.Pragmas)
        {
            if (pragma.Name.Value == "toplevel" && pragma.Value.Value == "enable")
                return pragma.HashToken.Span;
        }

        return unit.EndToken.Span;
    }

    private static bool IsUserFacingError(Exception ex) =>
        ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or
              PathTooLongException or NotSupportedException or ArgumentException;

    private static string FormatFileError(string filePath, Exception ex) => ex switch
    {
        FileNotFoundException => $"source file not found: {filePath}.",
        DirectoryNotFoundException => $"directory not found: {filePath}.",
        UnauthorizedAccessException => $"access denied while trying to analyze the file: {filePath}.",
        PathTooLongException => $"path is too long: {filePath}.",
        NotSupportedException => $"path format is not supported: {filePath}.",
        ArgumentException => string.IsNullOrWhiteSpace(ex.Message) ? $"invalid path: {filePath}." : ex.Message,
        IOException => $"I/O error while trying to analyze the file: {ex.Message}",
        _ => ex.Message
    };
}
