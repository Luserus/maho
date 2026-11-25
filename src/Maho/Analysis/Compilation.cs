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
using Maho.Tooling;

namespace Maho;

/// <summary>
/// Represents an immutable compilation unit or project batch in the Maho compiler.
/// Coordinates lexing, parsing, multi-file syntax trees, project references, and semantic resolution.
/// </summary>
public sealed class Compilation
{
    /// <summary> Gets the project name or assembly identity for this compilation. </summary>
    public string ProjectName { get; }

    /// <summary> Gets the canonical PascalCase symbolic module name for this compilation. </summary>
    public string ModuleName => Context.ModuleName;

    /// <summary> Gets the ordered parsed syntax trees for all source files in this compilation. </summary>
    public IReadOnlyList<SyntaxTree> SyntaxTrees { get; }

    /// <summary> Gets the ordered parsed syntax trees for all source files in this compilation. </summary>
    public IReadOnlyList<SyntaxTree> Files => SyntaxTrees;

    /// <summary> Gets the configuration options used during compilation. </summary>
    public CompilationOptions Options { get; }

    /// <summary> Gets the entry point source file path, if specified or inferred. </summary>
    public string? EntryFile { get; }

    /// <summary> Gets the referenced external project compilations. </summary>
    public IReadOnlyList<Compilation> ReferencedCompilations { get; }

    /// <summary> Gets the project-wide semantic resolution context. </summary>
    internal ResolutionContext Context { get; }

    /// <summary> Gets aggregated diagnostics reported across all phases of compilation. </summary>
    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; }

    /// <summary> Gets the compiled artifact containing generated MHIL instructions. </summary>
    public CompiledArtifact Artifact { get; }

    /// <summary> Gets execution timers for each compiler pipeline phase. </summary>
    public CompilationPhaseTimers PhaseTimers { get; }

    /// <summary> Gets total elapsed compilation time across all phases. </summary>
    public TimeSpan Elapsed => PhaseTimers.Total;

    /// <summary> Gets file I/O or access errors keyed by file path. </summary>
    public IReadOnlyDictionary<string, (string ErrorMessage, bool IsInternal)> FileErrors { get; }

    /// <summary> Indicates whether any fatal error or escalated warning diagnostics were emitted. </summary>
    public bool HasErrors => Diagnostics.Any(d =>
        d.Severity == DiagnosticSeverity.Error ||
        (Options.WarningsAsErrors && d.Severity == DiagnosticSeverity.Warning));

    /// <summary>
    /// Gets the primary source path if this compilation represents a single file, or the entry file.
    /// </summary>
    public string? SourcePath => SyntaxTrees.Count == 1 ? SyntaxTrees[0].FilePath : EntryFile;


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

    private readonly Dictionary<SyntaxTree, SemanticModel> semanticModels = [];
    private readonly Dictionary<string, SemanticModel> semanticModelsByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the semantic model for a syntax tree in this compilation.
    /// </summary>
    /// <param name="syntaxTree">The syntax tree whose semantic model to retrieve.</param>
    /// <returns>The <see cref="SemanticModel"/>, or <see langword="null"/> if not present.</returns>
    public SemanticModel? GetSemanticModel(SyntaxTree syntaxTree) =>
        semanticModels.TryGetValue(syntaxTree, out var model) ? model : null;

    /// <summary>
    /// Gets the semantic model for a source file path in this compilation.
    /// </summary>
    /// <param name="filePath">The source file path whose semantic model to retrieve.</param>
    /// <returns>The <see cref="SemanticModel"/>, or <see langword="null"/> if not present.</returns>
    public SemanticModel? GetSemanticModel(string filePath) =>
        semanticModelsByPath.TryGetValue(filePath, out var model) ? model : null;

    /// <summary>
    /// LSP Go-to-Definition: resolves definition location for a symbol at the given file and position.
    /// </summary>
    /// <param name="filePath">The source file path containing the symbol reference.</param>
    /// <param name="line">The 1-based line number.</param>
    /// <param name="column">The 1-based column number.</param>
    /// <returns>A <see cref="DefinitionLocation"/> if resolved; otherwise <see langword="null"/>.</returns>
    public DefinitionLocation? FindDefinition(string filePath, int line, int column) =>
        GetSemanticModel(filePath)?.FindDefinition(line, column);

    /// <summary>
    /// LSP Find-All-References: finds all references across the compilation for a symbol at the given position.
    /// </summary>
    /// <param name="filePath">The source file path containing the symbol reference.</param>
    /// <param name="line">The 1-based line number.</param>
    /// <param name="column">The 1-based column number.</param>
    /// <param name="includeDeclaration">Whether to include the symbol's declaration in the results.</param>
    /// <returns>A list of reference locations matching the target symbol.</returns>
    public IReadOnlyList<ReferenceLocation> FindReferences(string filePath, int line, int column, bool includeDeclaration = true) =>
        GetSemanticModel(filePath)?.FindReferences(line, column, includeDeclaration) ?? [];

    /// <summary>
    /// LSP Hover: gets hover tooltip information for a symbol or expression at the given position.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <param name="line">The 1-based line number.</param>
    /// <param name="column">The 1-based column number.</param>
    /// <returns>Hover tooltip details, or <see langword="null"/> if no symbol or expression is at the position.</returns>
    public HoverInfo? GetHover(string filePath, int line, int column) =>
        GetSemanticModel(filePath)?.GetHover(line, column);

    /// <summary>
    /// LSP Document Symbols: retrieves hierarchical outline symbols for the given file.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <returns>A list of document outline symbols.</returns>
    public IReadOnlyList<DocumentSymbolInfo> GetDocumentSymbols(string filePath) =>
        GetSemanticModel(filePath)?.GetDocumentSymbols() ?? [];

    /// <summary>
    /// LSP Completions: provides completion items at the given file and position.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <param name="line">The 1-based line number.</param>
    /// <param name="column">The 1-based column number.</param>
    /// <returns>A list of completion suggestions.</returns>
    public IReadOnlyList<CompletionItemInfo> GetCompletions(string filePath, int line, int column) =>
        GetSemanticModel(filePath)?.GetCompletions(line, column) ?? [];

    /// <summary>
    /// LSP Signature Help: provides signature help and active parameter info at the given position.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <param name="line">The 1-based line number.</param>
    /// <param name="column">The 1-based column number.</param>
    /// <returns>Signature help information for the invocation, or <see langword="null"/> if not within a call.</returns>
    public SignatureHelpInfo? GetSignatureHelp(string filePath, int line, int column) =>
        GetSemanticModel(filePath)?.GetSignatureHelp(line, column);

    /// <summary>
    /// LSP Semantic Tokens: generates semantic classification tokens for the given file.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <returns>A list of classified semantic tokens for editor syntax highlighting.</returns>
    public IReadOnlyList<SemanticTokenInfo> GetSemanticTokens(string filePath) =>
        GetSemanticModel(filePath)?.GetSemanticTokens() ?? [];

    /// <summary>
    /// Creates a new compilation snapshot with modified options, preserving existing semantic models.
    /// </summary>
    /// <param name="newOptions">The new compilation options to apply.</param>
    /// <returns>A new <see cref="Compilation"/> snapshot with updated options.</returns>
    public Compilation WithOptions(CompilationOptions newOptions)
    {
        if (Options == newOptions) return this;
        return new Compilation(
            ProjectName,
            SyntaxTrees,
            newOptions,
            ReferencedCompilations,
            Context,
            Diagnostics,
            Artifact,
            CompilationPhaseTimers.Zero,
            FileErrors,
            semanticModels,
            EntryFile);
    }

    /// <summary>
    /// Creates a new compilation snapshot with modified phase timers, preserving all analysis results.
    /// </summary>
    /// <param name="newTimers">The new phase execution timings.</param>
    /// <returns>A new <see cref="Compilation"/> snapshot with updated phase timers.</returns>
    public Compilation WithPhaseTimers(CompilationPhaseTimers newTimers)
    {
        return new Compilation(
            ProjectName,
            SyntaxTrees,
            Options,
            ReferencedCompilations,
            Context,
            Diagnostics,
            Artifact,
            newTimers,
            FileErrors,
            semanticModels,
            EntryFile);
    }

    internal Compilation(
        string projectName,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        CompilationOptions options,
        IReadOnlyList<Compilation> referencedCompilations,
        ResolutionContext context,
        IReadOnlyList<DiagnosticInfo> diagnostics,
        CompiledArtifact? artifact = null,
        CompilationPhaseTimers? phaseTimers = null,
        IReadOnlyDictionary<string, (string ErrorMessage, bool IsInternal)>? fileErrors = null,
        IReadOnlyDictionary<SyntaxTree, SemanticModel>? models = null,
        string? entryFile = null)
    {
        ProjectName = projectName;
        SyntaxTrees = syntaxTrees;
        Options = options;
        EntryFile = entryFile ?? options.EntryFile;
        ReferencedCompilations = referencedCompilations;
        Context = context;
        Diagnostics = diagnostics;
        Artifact = artifact ?? CompiledArtifact.Empty;
        PhaseTimers = phaseTimers ?? CompilationPhaseTimers.Zero;
        FileErrors = fileErrors ?? new Dictionary<string, (string, bool)>();

        foreach (var tree in syntaxTrees)
        {
            SemanticModel? model = null;
            if (models != null && models.TryGetValue(tree, out var existing))
            {
                model = existing;
            }
            else
            {
                var bindings = context.ExtractTreeBindings(tree);
                model = new SemanticModel(this, tree, bindings.NodeSymbols, bindings.ExpressionTypes, bindings.ControlFlowGraphs);
            }

            semanticModels[tree] = model;
            semanticModelsByPath[tree.FilePath] = model;
        }
    }

    /// <summary>
    /// Creates a compilation from a single in-memory source string, with optional cached compilation reuse.
    /// </summary>
    /// <param name="source">The in-memory source text string.</param>
    /// <param name="filePath">The logical or file path to associate with this source.</param>
    /// <param name="options">Optional compilation configuration options.</param>
    /// <param name="referencedCompilations">Optional external referenced project compilations.</param>
    /// <param name="cachedCompilation">Optional previously resolved compilation to reuse AST and semantics.</param>
    /// <returns>A fully resolved <see cref="Compilation"/> instance.</returns>
    public static Compilation FromSource(
        string source,
        string filePath = "source.mh",
        CompilationOptions? options = null,
        IReadOnlyList<Compilation>? referencedCompilations = null,
        Compilation? cachedCompilation = null)
    {
        options ??= CompilationOptions.Default;

        string rawModuleBase = Path.GetFileNameWithoutExtension(filePath);
        string resolvedModuleName = !string.IsNullOrWhiteSpace(options.ModuleName)
            ? CompilationOptions.NormalizeModuleIdentifier(options.ModuleName)
            : CompilationOptions.NormalizeModuleIdentifier(rawModuleBase);
        options = string.Equals(options.ModuleName, resolvedModuleName, StringComparison.Ordinal)
            ? options
            : options with { ModuleName = resolvedModuleName };

        if (cachedCompilation != null &&
            !cachedCompilation.Options.RequiresReanalysis(options) &&
            cachedCompilation.SyntaxTrees.Count == 1 &&
            string.Equals(cachedCompilation.SyntaxTrees[0].FilePath, filePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(cachedCompilation.SyntaxTrees[0].SourceText.ToString(), source, StringComparison.Ordinal))
        {
            if (cachedCompilation.Options.WarningsAsErrors != options.WarningsAsErrors ||
                cachedCompilation.Options.ColorMode != options.ColorMode ||
                cachedCompilation.Options.PathStyle != options.PathStyle ||
                cachedCompilation.Options.OutputFormat != options.OutputFormat)
            {
                return cachedCompilation.WithOptions(options);
            }
            return cachedCompilation;
        }
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
            rawModuleBase,
            [syntaxTree],
            options,
            referencedCompilations ?? [],
            context,
            projectedDiagnostics,
            artifact: null,
            phaseTimers: phaseTimers);
    }

    /// <summary>
    /// Creates a compilation from a collection of source files, with optional incremental caching.
    /// </summary>
    /// <param name="filePaths">The collection of source file paths on disk.</param>
    /// <param name="projectName">Optional explicit project or assembly name.</param>
    /// <param name="options">Optional compilation configuration options.</param>
    /// <param name="referencedCompilations">Optional external referenced project compilations.</param>
    /// <param name="cachedCompilation">Optional previously resolved compilation to reuse unchanged AST trees and semantics.</param>
    /// <returns>A fully resolved <see cref="Compilation"/> instance.</returns>
    public static Compilation FromFiles(
        IEnumerable<string> filePaths,
        string? projectName = null,
        CompilationOptions? options = null,
        IReadOnlyList<Compilation>? referencedCompilations = null,
        Compilation? cachedCompilation = null)
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

        string resolvedFileModuleName = !string.IsNullOrWhiteSpace(options.ModuleName)
            ? CompilationOptions.NormalizeModuleIdentifier(options.ModuleName)
            : CompilationOptions.NormalizeModuleIdentifier(resolvedProjectName);
        options = string.Equals(options.ModuleName, resolvedFileModuleName, StringComparison.Ordinal)
            ? options
            : options with { ModuleName = resolvedFileModuleName };

        // 1. Full Reuse Short-Circuit:
        if (cachedCompilation != null &&
            !cachedCompilation.Options.RequiresReanalysis(options) &&
            pathsList.Count == cachedCompilation.SyntaxTrees.Count &&
            pathsList.All(p => cachedCompilation.GetSyntaxTree(p) is { } tree && tree.IsEquivalent(p)))
        {
            if (cachedCompilation.Options.WarningsAsErrors != options.WarningsAsErrors ||
                cachedCompilation.Options.ColorMode != options.ColorMode ||
                cachedCompilation.Options.PathStyle != options.PathStyle ||
                cachedCompilation.Options.OutputFormat != options.OutputFormat)
            {
                return cachedCompilation.WithOptions(options);
            }
            return cachedCompilation;
        }

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

        var changedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cachedTreeMap = new Dictionary<string, SyntaxTree>(StringComparer.OrdinalIgnoreCase);
        if (cachedCompilation != null && !cachedCompilation.Options.RequiresReanalysis(options))
        {
            foreach (var t in cachedCompilation.SyntaxTrees)
                cachedTreeMap[t.FilePath] = t;
        }

        var syntaxSw = Stopwatch.StartNew();
        Parallel.For(0, pathsList.Count, i =>
        {
            string path = pathsList[i];
            if (cachedTreeMap.TryGetValue(path, out var cachedTree) && cachedTree.IsEquivalent(path))
            {
                sourceTexts[i] = cachedTree.SourceText;
                roots[i] = cachedTree.Root;
                fileDms[i] = cachedTree.DiagnosticsManager;
            }
            else
            {
                lock (changedPaths) { changedPaths.Add(path); }
                try
                {
                    var text = new SourceText(new SourceFile(path));
                    sourceTexts[i] = text;

                    bool isEntryFile = options.EntryFile != null &&
                        (string.Equals(path, options.EntryFile, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Path.GetFileName(path), options.EntryFile, StringComparison.OrdinalIgnoreCase));

                    bool isSingleFileScript = pathsList.Count == 1 && options.RootDirectory == null;
                    bool allowImplicit = (isSingleFileScript && (options.EntryFile == null || isEntryFile)) ||
                                         (options.ImplicitTopLevel && isEntryFile);

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

        // Determine which files have top-level statements enabled.
        // Top-level status is determined strictly by explicit configuration (options.EntryFile matching
        // this file AND options.ImplicitTopLevel is true, or single-file script) or explicit directive (#pragma toplevel enable).
        string? effectiveEntryFile = options.EntryFile;
        List<int> explicitTopLevelIndices = [];

        for (int i = 0; i < validRoots.Count; i++)
        {
            var unit = validRoots[i];
            bool? pragmaState = PragmaDirective.GetTopLevelPragmaState(unit.Pragmas);
            bool isExplicitEntry = options.EntryFile != null &&
                (string.Equals(validPaths[i], options.EntryFile, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(Path.GetFileName(validPaths[i]), options.EntryFile, StringComparison.OrdinalIgnoreCase));
            bool isSingleFileScript = pathsList.Count == 1 && options.RootDirectory == null;

            if (pragmaState == false)
            {
                unit.EnablesTopLevelStatements = false;
            }
            else if (pragmaState == true)
            {
                unit.EnablesTopLevelStatements = true;
                explicitTopLevelIndices.Add(i);
            }
            else
            {
                if ((options.ImplicitTopLevel && isExplicitEntry) || (isSingleFileScript && (options.EntryFile == null || isExplicitEntry)))
                {
                    unit.EnablesTopLevelStatements = true;
                    explicitTopLevelIndices.Add(i);
                }
                else
                {
                    unit.EnablesTopLevelStatements = false;
                }
            }
        }

        if (effectiveEntryFile == null)
        {
            if (pathsList.Count == 1 && options.RootDirectory == null)
                effectiveEntryFile = pathsList[0];
            else if (explicitTopLevelIndices.Count == 1)
                effectiveEntryFile = validPaths[explicitTopLevelIndices[0]];
        }

        if (explicitTopLevelIndices.Count > 1)
        {
            foreach (int idx in explicitTopLevelIndices)
                validFileDms[idx].ReportMultipleTopLevelSources(GetTopLevelPragmaSpan(validRoots[idx]));
        }

        var effectiveOptions = options with
        {
            EntryFile = effectiveEntryFile,
            ImplicitTopLevel = options.ImplicitTopLevel || (pathsList.Count == 1 && options.RootDirectory == null)
        };

        var syntaxTrees = new SyntaxTree[validRoots.Count];

        for (int i = 0; i < validRoots.Count; i++)
        {
            if (cachedTreeMap.TryGetValue(validPaths[i], out var cachedTree) &&
                ReferenceEquals(cachedTree.Root, validRoots[i]))
            {
                syntaxTrees[i] = cachedTree;
            }
            else
            {
                syntaxTrees[i] = new SyntaxTree(validRoots[i], validSourceTexts[i], validFileDms[i], validPaths[i]);
            }
        }

        var resolver = new Resolver();
        var referencedContexts = referencedCompilations
            .Where(c => c.Context != null)
            .Select(c => c.Context!)
            .ToList();

        var resolutionDm = new DiagnosticsManager();
        var context = resolver.ResolveIncremental(syntaxTrees, changedPaths, referencedContexts, cachedCompilation, diagnostics: resolutionDm, options: effectiveOptions);

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

        // Check if any changed file changed public signatures
        bool signaturesChanged = false;
        if (cachedCompilation == null || changedPaths.Count == pathsList.Count)
        {
            signaturesChanged = true;
        }
        else
        {
            foreach (var changedPath in changedPaths)
            {
                var oldSymbols = cachedCompilation.Context.GetTreeSymbols(changedPath);
                var newSymbols = context.GetTreeSymbols(changedPath);
                if (oldSymbols.Count != newSymbols.Count)
                {
                    signaturesChanged = true;
                    break;
                }
                for (int s = 0; s < oldSymbols.Count; s++)
                {
                    var oldSym = cachedCompilation.Context.GetSymbol(oldSymbols[s]);
                    var newSym = context.GetSymbol(newSymbols[s]);
                    if (oldSym?.Name.Text != newSym?.Name.Text || oldSym?.Kind != newSym?.Kind)
                    {
                        signaturesChanged = true;
                        break;
                    }
                }
                if (signaturesChanged)
                    break;
            }
        }

        var reusableModels = new Dictionary<SyntaxTree, SemanticModel>();
        if (!signaturesChanged && cachedCompilation != null)
        {
            foreach (var tree in syntaxTrees)
            {
                if (!changedPaths.Contains(tree.FilePath) && cachedCompilation.GetSemanticModel(tree.FilePath) is { } cachedModel)
                {
                    reusableModels[tree] = cachedModel;
                }
            }
        }

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
            options,
            referencedCompilations,
            context,
            orderedProjected,
            artifact: null,
            phaseTimers: phaseTimers,
            fileErrors: fileErrorsMap,
            models: reusableModels,
            entryFile: effectiveEntryFile);
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
