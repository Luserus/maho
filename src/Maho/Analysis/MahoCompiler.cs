using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Maho.Diagnostics;
using Maho.Resolution;
using Maho.Syntax;
using Maho.Text;

namespace Maho;

/// <summary>
/// Public entrypoint into the compiler front-end. This type coordinates source loading, syntax
/// analysis, diagnostics projection, and optional debug serialization into one stable API surface.
/// </summary>
public static class MahoCompiler
{
    /// <summary>
    /// Holds the syntax-stage artifacts for one file until the compiler is ready to assemble the
    /// project syntax tree and run project-wide resolution.
    /// </summary>
    private sealed class ParsedFileAnalysis
    {
        /// <summary> Stable source identity for the parsed file. </summary>
        public string SourcePath { get; }
        /// <summary>
        /// Source text backing all tokens/nodes in <see cref="Root"/>. This must stay alive until
        /// every later phase that touches syntax has finished.
        /// </summary>
        public SourceText Text { get; }
        /// <summary>
        /// Shared lexer/parser diagnostic sink for this file. Keeping one sink per file makes it
        /// possible to batch parse in parallel without cross-file diagnostic interleaving.
        /// </summary>
        public DiagnosticsManager Diagnostics { get; }
        /// <summary> Parsed syntax root for the file. </summary>
        public CompilationUnit Root { get; }
        /// <summary> Optional serialized lexer debug payload captured while the text is still alive. </summary>
        public string? LexerJson { get; }
        /// <summary> Optional serialized parser debug payload captured while the text is still alive. </summary>
        public string? ParserJson { get; }

        /// <summary> Creates the deferred analysis package for one successfully parsed file. </summary>
        public ParsedFileAnalysis(
            string sourcePath,
            SourceText text,
            DiagnosticsManager diagnostics,
            CompilationUnit root,
            string? lexerJson,
            string? parserJson)
        {
            SourcePath = sourcePath;
            Text = text;
            Diagnostics = diagnostics;
            Root = root;
            LexerJson = lexerJson;
            ParserJson = parserJson;
        }
    }

    /// <summary>
    /// Analyzes a source file from disk and returns diagnostics plus any requested debug artifacts.
    /// The file path is normalized first so downstream consumers see a stable source identity.
    /// </summary>
    public static CompilerAnalysisResult AnalyzeFile(string filePath, AnalysisOutput output = AnalysisOutput.None)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Source file path cannot be empty.", nameof(filePath));

        string fullPath = Path.GetFullPath(filePath);

        SourceText text = new(new SourceFile(fullPath));

        try
        {
            return AnalyzeCore(text, fullPath, output);
        }
        finally
        {
            DisposeSourceText(text);
        }
    }

    /// <summary>
    /// Analyzes already-loaded source text, which is useful for tests, editor integrations, and
    /// other callers that do not want the compiler API to own file I/O.
    /// </summary>
    public static CompilerAnalysisResult AnalyzeText(string sourceText, AnalysisOutput output = AnalysisOutput.None, string sourcePath = "<memory>")
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path cannot be empty.", nameof(sourcePath));

        SourceText text = new(sourceText);

        try
        {
            return AnalyzeCore(text, sourcePath, output);
        }
        finally
        {
            DisposeSourceText(text);
        }
    }

    /// <summary>
    /// Analyzes a batch of files, letting the compiler own file-level syntax parallelism while
    /// still honoring the project-wide barrier before semantic resolution begins.
    /// </summary>
    public static CompilerProjectAnalysisResult AnalyzeFiles(IReadOnlyList<string> filePaths, AnalysisOutput output = AnalysisOutput.None, string projectName = "<project>")
    {
        return AnalyzeFilesCore(filePaths, output, projectName, explicitEntryFile: null);
    }

    /// <summary>
    /// Runs a complete compilation request for a collection of source files. Syntax and resolution
    /// diagnostics are returned normally; a successful front end continues into the next pipeline
    /// stage, which is currently a deliberate placeholder.
    /// </summary>
    public static CompilerProjectAnalysisResult CompileFiles(IReadOnlyList<string> filePaths, AnalysisOutput output = AnalysisOutput.None, string projectName = "<project>")
    {
        return ContinueCompilation(AnalyzeFiles(filePaths, output, projectName));
    }

    /// <summary>
    /// Loads an <c>.mhpr</c> project file, discovers its source files, and analyzes the
    /// project using its entry-point configuration.
    /// </summary>
    public static CompilerProjectAnalysisResult AnalyzeProjectFile(string projectFilePath, AnalysisOutput output = AnalysisOutput.None)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            throw new ArgumentException("Project file path cannot be empty.", nameof(projectFilePath));

        string fullProjectPath = Path.GetFullPath(projectFilePath);

        if (!string.Equals(Path.GetExtension(fullProjectPath), ".mhpr", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Project files must use the '.mhpr' extension.", nameof(projectFilePath));

        MahoProjectConfiguration configuration;

        try
        {
            string projectJson = File.ReadAllText(fullProjectPath);
            configuration = MahoProjectFileParser.Parse(projectJson);
        }
        catch (MahoProjectParseException ex)
        {
            throw new ArgumentException($"Project file is invalid: {ex.Message}", nameof(projectFilePath), ex);
        }

        string projectDirectory = Path.GetDirectoryName(fullProjectPath)!;
        string[] sourceFiles = Directory.GetFiles(projectDirectory, "*.mh", SearchOption.AllDirectories);
        Array.Sort(sourceFiles, StringComparer.Ordinal);

        if (sourceFiles.Length == 0)
            throw new FileNotFoundException("Project does not contain any '.mh' source files.", fullProjectPath);

        string? explicitEntryFile = ResolveProjectEntryFile(configuration, projectDirectory, sourceFiles, projectFilePath);
        string projectName = Path.GetFileNameWithoutExtension(fullProjectPath);
        return AnalyzeFilesCore(sourceFiles, output, projectName, explicitEntryFile);
    }

    /// <summary>
    /// Runs a complete compilation request described by a <c>.mhpr</c> project file. A successful
    /// front end continues into the next pipeline stage, which is currently a deliberate placeholder.
    /// </summary>
    public static CompilerProjectAnalysisResult CompileProjectFile(string projectFilePath, AnalysisOutput output = AnalysisOutput.None)
    {
        return ContinueCompilation(AnalyzeProjectFile(projectFilePath, output));
    }

    private static CompilerProjectAnalysisResult ContinueCompilation(CompilerProjectAnalysisResult analysis)
    {
        if (analysis.HasErrors)
            return analysis;

        LowerAndEmit(analysis);
        return analysis;
    }

    private static void LowerAndEmit(CompilerProjectAnalysisResult analysis)
    {
        throw new CompilerPipelineNotImplementedException(analysis);
    }

    private static CompilerProjectAnalysisResult AnalyzeFilesCore(IReadOnlyList<string> filePaths, AnalysisOutput output, string projectName, string? explicitEntryFile)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ArgumentException("Project name cannot be empty.", nameof(projectName));

        string[] normalizedPaths = new string[filePaths.Count];

        for (int i = 0; i < filePaths.Count; i++)
        {
            string filePath = filePaths[i];

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Source file path cannot be empty.", nameof(filePaths));

            normalizedPaths[i] = Path.GetFullPath(filePath);
        }

        CompilerBatchFileResult?[] results = new CompilerBatchFileResult[normalizedPaths.Length];
        ParsedFileAnalysis?[] parsedFiles = new ParsedFileAnalysis[normalizedPaths.Length];

        try
        {
            // Lexing and parsing are file-local, so they can be completed independently before the
            // compiler crosses the project-wide barrier into semantic resolution.
            Parallel.For(0, normalizedPaths.Length, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, index =>
            {
                string filePath = normalizedPaths[index];

                try
                {
                    SourceText text = new(new SourceFile(filePath));
                    parsedFiles[index] = ParseCore(text, filePath, output);
                }
                catch (Exception ex)
                {
                    results[index] = new CompilerBatchFileResult(filePath, null, FormatAnalysisError(ex, filePath), !IsUserFacingError(ex), HasErrors: true);
                }
            });

            int rootCount = 0;

            for (int i = 0; i < parsedFiles.Length; i++)
            {
                ParsedFileAnalysis? parsedFile = parsedFiles[i];

                if (parsedFile is not null)
                    rootCount++;
            }

            Diagnostic[] resolutionDiagnostics = [];
            string? selectedEntryFile = null;

            if (rootCount > 0)
            {
                CompilationUnit[] roots = new CompilationUnit[rootCount];
                int rootIndex = 0;

                for (int i = 0; i < parsedFiles.Length; i++)
                {
                    ParsedFileAnalysis? parsedFile = parsedFiles[i];

                    if (parsedFile is not null)
                        roots[rootIndex++] = parsedFile.Root;
                }

                DiagnosticsManager resolutionDiagnosticsManager = new();
                SyntaxTree syntaxTree = new(projectName, roots);
                selectedEntryFile = ValidateTopLevelEntryPoint(parsedFiles, explicitEntryFile);
                new Resolver().Resolve(syntaxTree);
                int resolutionDiagnosticCount = resolutionDiagnosticsManager.Diagnostics.Count;
                resolutionDiagnostics = new Diagnostic[resolutionDiagnosticCount];

                for (int i = 0; i < resolutionDiagnosticCount; i++)
                    resolutionDiagnostics[i] = resolutionDiagnosticsManager.Diagnostics[i];
            }

            for (int i = 0; i < normalizedPaths.Length; i++)
            {
                if (results[i] is not null)
                    continue;

                ParsedFileAnalysis parsedFile = parsedFiles[i]!;
                CompilerAnalysisResult analysis = CreateAnalysisResult(parsedFile, resolutionDiagnostics);
                results[i] = new CompilerBatchFileResult(parsedFile.SourcePath, analysis, null, IsInternalError: false, analysis.HasErrors);
            }

            CompilerBatchFileResult[] finalResults = new CompilerBatchFileResult[results.Length];

            for (int i = 0; i < results.Length; i++)
                finalResults[i] = results[i]!;

            return new CompilerProjectAnalysisResult(projectName, finalResults)
            {
                EntryFile = selectedEntryFile
            };
        }
        finally
        {
            for (int i = 0; i < parsedFiles.Length; i++)
            {
                if (parsedFiles[i] is ParsedFileAnalysis parsedFile)
                    DisposeSourceText(parsedFile.Text);
            }
        }
    }

    private static string? ResolveProjectEntryFile(MahoProjectConfiguration configuration, string projectDirectory, IReadOnlyList<string> sourceFiles, string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(configuration.EntryFile))
            return null;

        string entryFile = Path.GetFullPath(Path.Combine(projectDirectory, configuration.EntryFile));

        if (!File.Exists(entryFile))
            throw new FileNotFoundException("Configured EntryFile was not found.", entryFile);

        for (int index = 0; index < sourceFiles.Count; index++)
        {
            if (string.Equals(sourceFiles[index], entryFile, StringComparison.OrdinalIgnoreCase))
                return entryFile;
        }

        throw new ArgumentException("Configured EntryFile must be a '.mh' file within the project directory.", nameof(projectFilePath));
    }

    private static string? ValidateTopLevelEntryPoint(IReadOnlyList<ParsedFileAnalysis?> parsedFiles, string? explicitEntryFile)
    {
        List<ParsedFileAnalysis> candidates = [];

        for (int index = 0; index < parsedFiles.Count; index++)
        {
            ParsedFileAnalysis? parsedFile = parsedFiles[index];

            if (parsedFile is not null && PragmaDirective.EnablesTopLevelStatements(parsedFile.Root.Pragmas) && ContainsTopLevelStatement(parsedFile.Root.Members))
                candidates.Add(parsedFile);
        }

        if (candidates.Count > 1)
        {
            foreach (ParsedFileAnalysis candidate in candidates)
            {
                candidate.Diagnostics.ReportError("MH0012", "Only one source file may contain opted-in top-level statements.", GetTopLevelPragmaSpan(candidate.Root));
            }
        }

        if (explicitEntryFile is not null)
            return explicitEntryFile;

        return candidates.Count is 1 ? candidates[0].SourcePath : null;
    }

    private static bool ContainsTopLevelStatement(IReadOnlyList<TopLevel> members)
    {
        foreach (TopLevel member in members)
        {
            switch (member)
            {
                case TopLevelStatement:
                    return true;
                case TopLevelBlock block when ContainsTopLevelStatement(block.Members):
                    return true;
                case TopLevelGlobalBlock block when ContainsTopLevelStatement(block.Members):
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

    /// <summary>
    /// Runs the shared front-end pipeline against a prepared <see cref="SourceText"/> instance.
    /// Lexer and parser share one diagnostics manager so callers receive a single coherent report.
    /// </summary>
    private static CompilerAnalysisResult AnalyzeCore(SourceText text, string sourcePath, AnalysisOutput output)
    {
        ParsedFileAnalysis parsedFile = ParseCore(text, sourcePath, output);
        new Resolver().Resolve(SyntaxTree.CreateSingleRoot(parsedFile.Root, sourcePath));
        return CreateAnalysisResult(parsedFile);
    }

    /// <summary>
    /// Runs only the file-local syntax pipeline. The returned package deliberately keeps the source
    /// text and root alive so batch analysis can assemble a complete project syntax tree before
    /// starting resolution.
    /// </summary>
    private static ParsedFileAnalysis ParseCore(SourceText text, string sourcePath, AnalysisOutput output)
    {
        DiagnosticsManager diagnosticsManager = new(text);

        // Lexer and parser intentionally share one diagnostics sink so syntax-stage recovery keeps a
        // single ordered story per file before project-wide phases begin.
        Lexer lexer = new(text, diagnosticsManager);
        lexer.Lex();

        Parser parser = new(text, diagnosticsManager);
        CompilationUnit root = parser.Parse(lexer.Tokens);

        return new ParsedFileAnalysis(
            sourcePath,
            text,
            diagnosticsManager,
            root,
            output.HasFlag(AnalysisOutput.Lexer) ? lexer.ToString() : null,
            output.HasFlag(AnalysisOutput.Parser) ? parser.ToString() : null);
    }

    /// <summary>
    /// Finalizes one file result from its syntax-stage artifacts plus any project-wide diagnostics
    /// that were attributed back to the same source buffer during resolution.
    /// </summary>
    private static CompilerAnalysisResult CreateAnalysisResult(ParsedFileAnalysis parsedFile, Diagnostic[]? projectDiagnostics = null)
    {
        DiagnosticInfo[] diagnostics = CreateDiagnostics(parsedFile.Diagnostics.Diagnostics, parsedFile.Text, projectDiagnostics);

        return new CompilerAnalysisResult(
            parsedFile.SourcePath,
            parsedFile.LexerJson,
            parsedFile.ParserJson,
            diagnostics,
            DebugJson.Serialize(diagnostics));
    }

    /// <summary>
    /// Chooses whether a batch-analysis exception should be surfaced as a user-facing environment
    /// failure or as an internal compiler fault.
    /// </summary>
    private static bool IsUserFacingError(Exception ex) =>
        ex is ArgumentException
            or UnauthorizedAccessException
            or PathTooLongException
            or DirectoryNotFoundException
            or FileNotFoundException
            or IOException
            or NotSupportedException;

    /// <summary>
    /// Converts low-level file/path failures into stable batch-analysis text while preserving
    /// compiler-fault messages for actual internal exceptions.
    /// </summary>
    private static string FormatAnalysisError(Exception ex, string filePath)
    {
        if (!IsUserFacingError(ex))
            return ex.Message;

        return ex switch
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

    /// <summary>
    /// Projects internal diagnostics into the public result model, enriching raw spans with
    /// line/column information so consumers do not need the original source buffer.
    /// </summary>
    private static DiagnosticInfo[] CreateDiagnostics(IReadOnlyList<Diagnostic> fileDiagnostics, SourceText text, Diagnostic[]? projectDiagnostics = null)
    {
        int projectedCount = fileDiagnostics.Count;

        if (projectDiagnostics is not null)
        {
            for (int i = 0; i < projectDiagnostics.Length; i++)
            {
                if (ReferenceEquals(projectDiagnostics[i].Source, text))
                    projectedCount++;
            }
        }

        DiagnosticInfo[] diagnostics = new DiagnosticInfo[projectedCount];
        int outputIndex = 0;

        AppendDiagnostics(diagnostics, ref outputIndex, fileDiagnostics, text, filterBySource: false);

        if (projectDiagnostics is not null)
            AppendDiagnostics(diagnostics, ref outputIndex, projectDiagnostics, text, filterBySource: true);

        return diagnostics;
    }

    /// <summary>
    /// Appends projected diagnostics to one output buffer. Project-wide diagnostics are filtered by
    /// source identity so each file only receives the diagnostics that actually belong to it.
    /// </summary>
    private static void AppendDiagnostics(DiagnosticInfo[] output, ref int outputIndex, IReadOnlyList<Diagnostic> diagnostics, SourceText text, bool filterBySource)
    {
        for (int i = 0; i < diagnostics.Count; i++)
        {
            Diagnostic diagnostic = diagnostics[i];

            if (filterBySource && !ReferenceEquals(diagnostic.Source, text))
                continue;

            // This is the only place internal diagnostics become part of the public API contract, so
            // severity and span projection stay centralized here.
            output[outputIndex++] = new DiagnosticInfo(
                diagnostic.DiagnosticCode,
                diagnostic.Message,
                MapSeverity(diagnostic.Kind),
                CreateSpanInfo(diagnostic.Span, text),
                diagnostic.ExpectedText);
        }
    }

    /// <summary>
    /// Maps internal severity values onto the public severity contract exposed by the analysis API.
    /// </summary>
    private static DiagnosticSeverity MapSeverity(DiagnosticKind kind) => kind switch
    {
        DiagnosticKind.Info => DiagnosticSeverity.Info,
        DiagnosticKind.Warning => DiagnosticSeverity.Warning,
        DiagnosticKind.Error => DiagnosticSeverity.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled diagnostic kind.")
    };

    /// <summary>
    /// Converts an internal <see cref="TextSpan"/> into the public span contract used by
    /// diagnostics and debug payloads, pairing absolute offsets with one-based line/column data.
    /// </summary>
    internal static TextSpanInfo CreateSpanInfo(TextSpan span, SourceText text) =>
        new(
            span.Start,
            span.Length,
            span.End,
            new TextLocation(span.GetStartLine(text) + 1, span.GetStartColumn(text) + 1),
            new TextLocation(span.GetEndLine(text) + 1, span.GetEndColumn(text) + 1));

    /// <summary>
    /// Keeps disposal explicit without widening <see cref="SourceText"/> itself to the public
    /// <see cref="IDisposable"/> surface at every call site.
    /// </summary>
    private static void DisposeSourceText(SourceText text) => ((IDisposable)text).Dispose();
}
