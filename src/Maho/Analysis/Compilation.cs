using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Maho.Diagnostics;
using Maho.Resolution;
using Maho.Syntax;
using Maho.Text;

namespace Maho;

/// <summary>
/// Represents an immutable compilation unit or project batch in the Maho compiler.
/// Coordinates lexing, parsing, multi-file syntax trees, project references, and semantic resolution.
/// </summary>
public sealed class Compilation
{
    public string ProjectName { get; }
    public IReadOnlyList<SyntaxTree> SyntaxTrees { get; }
    public IReadOnlyList<SyntaxTree> Files => SyntaxTrees;
    public CompilationOptions Options { get; }
    public string? EntryFile => Options.EntryFile;
    public IReadOnlyList<Compilation> ReferencedCompilations { get; }
    internal ResolutionContext? Context { get; }
    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; }
    public CompiledArtifact Artifact { get; }
    public CompilationPhaseTimers PhaseTimers { get; }
    public TimeSpan Elapsed => PhaseTimers.Total;
    public IReadOnlyDictionary<string, (string ErrorMessage, bool IsInternal)> FileErrors { get; }

    public bool HasErrors => Diagnostics.Any(d =>
        d.Severity == DiagnosticSeverity.Error ||
        (Options.WarningsAsErrors && d.Severity == DiagnosticSeverity.Warning));

    /// <summary>
    /// Gets the primary source path if this compilation represents a single file, or the entry file.
    /// </summary>
    public string? SourcePath => SyntaxTrees.Count == 1 ? SyntaxTrees[0].FilePath : Options.EntryFile;


    /// <summary>
    /// Gets diagnostics across the compilation, optionally filtered by a specific source file path.
    /// </summary>
    public IReadOnlyList<DiagnosticInfo> GetDiagnostics(string? filePath = null)
    {
        if (string.IsNullOrEmpty(filePath))
            return Diagnostics;

        return Diagnostics.Where(d => string.Equals(d.SourcePath, filePath, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Finds a parsed syntax tree by its file path.
    /// </summary>
    public SyntaxTree? GetSyntaxTree(string filePath) =>
        SyntaxTrees.FirstOrDefault(t => string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

    internal Compilation(
        string projectName,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        CompilationOptions options,
        IReadOnlyList<Compilation> referencedCompilations,
        ResolutionContext? context,
        IReadOnlyList<DiagnosticInfo> diagnostics,
        CompiledArtifact? artifact = null,
        CompilationPhaseTimers? phaseTimers = null,
        IReadOnlyDictionary<string, (string ErrorMessage, bool IsInternal)>? fileErrors = null)
    {
        ProjectName = projectName;
        SyntaxTrees = syntaxTrees;
        Options = options;
        ReferencedCompilations = referencedCompilations;
        Context = context;
        Diagnostics = diagnostics;
        Artifact = artifact ?? CompiledArtifact.Empty;
        PhaseTimers = phaseTimers ?? CompilationPhaseTimers.Zero;
        FileErrors = fileErrors ?? new Dictionary<string, (string, bool)>();
    }

    /// <summary>
    /// Creates a compilation from a single in-memory source string.
    /// </summary>
    public static Compilation FromSource(
        string source,
        string filePath = "source.mh",
        CompilationOptions? options = null,
        IReadOnlyList<Compilation>? referencedCompilations = null)
    {
        options ??= CompilationOptions.Default;
        var sourceText = new SourceText(source);
        var diagnosticsManager = new DiagnosticsManager(sourceText);

        var syntaxSw = Stopwatch.StartNew();
        var lexer = new Lexer(sourceText, diagnosticsManager);
        lexer.Lex();

        var parser = new Parser(sourceText, diagnosticsManager, options.ImplicitTopLevel);
        var unit = parser.Parse(lexer.Tokens);
        syntaxSw.Stop();

        var semSw = Stopwatch.StartNew();
        var syntaxTree = new SyntaxTree(unit, sourceText, diagnosticsManager, filePath);
        var resolver = new Resolver();
        var refContexts = referencedCompilations?.Select(c => c.Context).OfType<ResolutionContext>().ToList();
        var context = resolver.Resolve(syntaxTree, referencedProjects: refContexts, diagnostics: diagnosticsManager, options: options);
        semSw.Stop();

        var projectedDiagnostics = ProjectDiagnostics(diagnosticsManager.Diagnostics, sourceText, filePath);
        var phaseTimers = new CompilationPhaseTimers(syntaxSw.Elapsed, semSw.Elapsed, TimeSpan.Zero);

        return new Compilation(
            Path.GetFileNameWithoutExtension(filePath),
            [syntaxTree],
            options,
            referencedCompilations ?? [],
            context,
            projectedDiagnostics,
            artifact: null,
            phaseTimers: phaseTimers);
    }

    /// <summary>
    /// Creates a compilation from a collection of source files.
    /// </summary>
    public static Compilation FromFiles(
        IEnumerable<string> filePaths,
        string? projectName = null,
        CompilationOptions? options = null,
        IReadOnlyList<Compilation>? referencedCompilations = null)
    {
        options ??= CompilationOptions.Default;
        referencedCompilations ??= [];
        var pathsList = filePaths.Select(Path.GetFullPath).ToList();

        string resolvedProjectName = projectName
            ?? (!string.IsNullOrWhiteSpace(options.RootDirectory) ? Path.GetFileName(options.RootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : null)
            ?? (pathsList.Count > 0 ? Path.GetFileNameWithoutExtension(pathsList[0]) : null)
            ?? "Project";

        if (string.IsNullOrEmpty(resolvedProjectName))
            resolvedProjectName = "Project";

        if (pathsList.Count == 0)
        {
            var refContexts = referencedCompilations.Where(c => c.Context != null).Select(c => c.Context!).ToList();
            var emptyContext = new Resolver().Resolve([], refContexts, diagnostics: new DiagnosticsManager(), options: options);
            return new Compilation(
                resolvedProjectName,
                [],
                options,
                referencedCompilations,
                emptyContext,
                [],
                artifact: null,
                phaseTimers: CompilationPhaseTimers.Zero);
        }

        var fileReadErrors = new (string Path, string ErrorMessage, bool IsInternal)?[pathsList.Count];
        var sourceTexts = new SourceText?[pathsList.Count];
        var roots = new CompilationUnit?[pathsList.Count];
        var fileDms = new DiagnosticsManager?[pathsList.Count];

        var syntaxSw = Stopwatch.StartNew();
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

                var parser = new Parser(text, dm, allowImplicit);
                var root = parser.Parse(lexer.Tokens);
                roots[i] = root;
            }
            catch (Exception ex)
            {
                fileReadErrors[i] = (path, FormatFileError(path, ex), !IsUserFacingError(ex));
            }
        });
        syntaxSw.Stop();

        var semSw = Stopwatch.StartNew();
        var validPaths = new List<string>(pathsList.Count);
        var validSourceTexts = new List<SourceText>(pathsList.Count);
        var validRoots = new List<CompilationUnit>(pathsList.Count);
        var validFileDms = new List<DiagnosticsManager>(pathsList.Count);

        for (int i = 0; i < pathsList.Count; i++)
        {
            if (fileReadErrors[i] is null)
            {
                validPaths.Add(pathsList[i]);
                validSourceTexts.Add(sourceTexts[i]!);
                validRoots.Add(roots[i]!);
                validFileDms.Add(fileDms[i]!);
            }
        }

        // Validate top-level statements and resolve the entry file candidate
        string? effectiveEntryFile = options.EntryFile;
        List<(int Index, CompilationUnit Root, DiagnosticsManager Dm)> filesWithTopLevel = [];

        for (int i = 0; i < validRoots.Count; i++)
        {
            var unit = validRoots[i];
            bool? pragmaState = PragmaDirective.GetTopLevelPragmaState(unit.Pragmas);
            bool hasStatements = ContainsTopLevelStatement(unit.Members);
            bool isExplicitEntry = options.EntryFile != null &&
                (string.Equals(validPaths[i], options.EntryFile, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(Path.GetFileName(validPaths[i]), options.EntryFile, StringComparison.OrdinalIgnoreCase));

            if (pragmaState == false)
            {
                unit.EnablesTopLevelStatements = false;
            }
            else if (pragmaState == true)
            {
                unit.EnablesTopLevelStatements = true;
                if (hasStatements)
                    filesWithTopLevel.Add((i, unit, validFileDms[i]));
            }
            else
            {
                if (options.ImplicitTopLevel)
                {
                    if (options.EntryFile != null)
                    {
                        if (isExplicitEntry)
                        {
                            unit.EnablesTopLevelStatements = true;
                            if (hasStatements)
                                filesWithTopLevel.Add((i, unit, validFileDms[i]));
                        }
                        else
                        {
                            unit.EnablesTopLevelStatements = false;
                            if (hasStatements)
                                filesWithTopLevel.Add((i, unit, validFileDms[i]));
                        }
                    }
                    else
                    {
                        if (hasStatements)
                            filesWithTopLevel.Add((i, unit, validFileDms[i]));

                        unit.EnablesTopLevelStatements = false;
                    }
                }
                else
                {
                    unit.EnablesTopLevelStatements = false;
                }
            }
        }

        if (effectiveEntryFile == null && filesWithTopLevel.Count == 1)
        {
            int winnerIdx = filesWithTopLevel[0].Index;
            validRoots[winnerIdx].EnablesTopLevelStatements = true;
            effectiveEntryFile = validPaths[winnerIdx];
        }
        else if (pathsList.Count == 1 && options.RootDirectory == null && effectiveEntryFile == null)
        {
            effectiveEntryFile = pathsList[0];
            if (validRoots.Count > 0 && PragmaDirective.GetTopLevelPragmaState(validRoots[0].Pragmas) != false)
                validRoots[0].EnablesTopLevelStatements = true;
        }

        if (filesWithTopLevel.Count > 1)
        {
            foreach (var (_, candRoot, candDm) in filesWithTopLevel)
                candDm.ReportMultipleTopLevelSources(GetTopLevelPragmaSpan(candRoot));
        }

        var effectiveOptions = options with
        {
            EntryFile = effectiveEntryFile,
            ImplicitTopLevel = options.ImplicitTopLevel || (pathsList.Count == 1 && options.RootDirectory == null)
        };

        var syntaxTrees = new SyntaxTree[validRoots.Count];
        for (int i = 0; i < validRoots.Count; i++)
            syntaxTrees[i] = new SyntaxTree(validRoots[i], validSourceTexts[i], validFileDms[i], validPaths[i]);

        var resolver = new Resolver();
        var referencedContexts = referencedCompilations
            .Where(c => c.Context != null)
            .Select(c => c.Context!)
            .ToList();

        var resolutionDm = new DiagnosticsManager();
        var context = resolver.Resolve(syntaxTrees, referencedContexts, diagnostics: resolutionDm, options: effectiveOptions);

        foreach (var diag in resolutionDm.Diagnostics)
        {
            int targetIdx = -1;
            for (int k = 0; k < validSourceTexts.Count; k++)
            {
                if (ReferenceEquals(diag.Source, validSourceTexts[k]))
                {
                    targetIdx = k;
                    break;
                }
            }
            if (targetIdx >= 0)
                validFileDms[targetIdx].Report(diag);
            else if (validFileDms.Count > 0)
                validFileDms[0].Report(diag);
        }
        semSw.Stop();

        var allProjectedDiagnostics = new List<DiagnosticInfo>();
        for (int i = 0; i < validPaths.Count; i++)
        {
            var dm = validFileDms[i];
            var text = validSourceTexts[i];
            var path = validPaths[i];
            allProjectedDiagnostics.AddRange(ProjectDiagnostics(dm.Diagnostics, text, path));
        }

        var fileErrorsMap = new Dictionary<string, (string ErrorMessage, bool IsInternal)>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < pathsList.Count; i++)
        {
            if (fileReadErrors[i] is { } err)
            {
                fileErrorsMap[err.Path] = (err.ErrorMessage, err.IsInternal);
                allProjectedDiagnostics.Add(new DiagnosticInfo(
                    "MH0000",
                    err.ErrorMessage,
                    DiagnosticSeverity.Error,
                    new TextSpanInfo(0, 0, 0, new TextLocation(1, 1), new TextLocation(1, 1)),
                    SourcePath: err.Path));
            }
        }

        var orderedProjected = DiagnosticInfo.OrderDiagnostics(allProjectedDiagnostics);
        var phaseTimers = new CompilationPhaseTimers(syntaxSw.Elapsed, semSw.Elapsed, TimeSpan.Zero);

        return new Compilation(
            resolvedProjectName,
            syntaxTrees,
            effectiveOptions,
            referencedCompilations,
            context,
            orderedProjected,
            artifact: null,
            phaseTimers: phaseTimers,
            fileErrors: fileErrorsMap);
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

    private static bool IsUserFacingError(Exception ex) => ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or
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

    /// <summary>
    /// Spawns an interactive <see cref="AnalysisSession"/> rooted in this compilation.
    /// </summary>
    public AnalysisSession CreateSession() => new(this);

    /// <summary>Reserved entry point for future editor semantic queries.</summary>
    internal SemanticModel GetSemanticModel(SyntaxTree syntaxTree) =>
        throw new NotImplementedException("Semantic-model queries are not implemented yet.");

    private static List<DiagnosticInfo> ProjectDiagnostics(
        IReadOnlyList<Diagnostic> diagnostics,
        SourceText sourceText,
        string filePath)
    {
        var result = new List<DiagnosticInfo>(diagnostics.Count);
        foreach (var diag in diagnostics)
            result.Add(DiagnosticInfo.FromDiagnostic(diag, sourceText, filePath));
        return DiagnosticInfo.OrderDiagnostics(result);
    }
}
