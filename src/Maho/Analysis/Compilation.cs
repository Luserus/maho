using System;
using System.Collections.Generic;
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
    internal IReadOnlyList<SyntaxTree> SyntaxTrees { get; }
    internal IReadOnlyList<SourceText> SourceTexts { get; }
    public CompilationOptions Options { get; }
    public IReadOnlyList<Compilation> ReferencedCompilations { get; }
    internal ResolutionContext? Context { get; }
    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; }

    public bool HasErrors => Diagnostics.Any(d =>
        d.Severity == DiagnosticSeverity.Error ||
        (Options.WarningsAsErrors && d.Severity == DiagnosticSeverity.Warning));

    private Compilation(
        string projectName,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        IReadOnlyList<SourceText> sourceTexts,
        CompilationOptions options,
        IReadOnlyList<Compilation> referencedCompilations,
        ResolutionContext? context,
        IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        ProjectName = projectName;
        SyntaxTrees = syntaxTrees;
        SourceTexts = sourceTexts;
        Options = options;
        ReferencedCompilations = referencedCompilations;
        Context = context;
        Diagnostics = diagnostics;
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

        var lexer = new Lexer(sourceText, diagnosticsManager);
        lexer.Lex();

        var parser = new Parser(sourceText, diagnosticsManager);
        var unit = parser.Parse(lexer.Tokens);

        var syntaxTree = SyntaxTree.CreateSingleRoot(unit, filePath);
        var resolver = new Resolver();
        var refContexts = referencedCompilations?.Select(c => c.Context).OfType<ResolutionContext>().ToList();
        var context = resolver.Resolve(syntaxTree, referencedProjects: refContexts, diagnostics: diagnosticsManager, options: options);

        var projectedDiagnostics = ProjectDiagnostics(diagnosticsManager.Diagnostics, sourceText, filePath);

        return new Compilation(
            Path.GetFileNameWithoutExtension(filePath),
            [syntaxTree],
            [sourceText],
            options,
            referencedCompilations ?? [],
            context,
            projectedDiagnostics);
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
        projectName ??= pathsList.Count > 0 ? Path.GetFileNameWithoutExtension(pathsList[0]) : "Project";

        var sourceTexts = new SourceText[pathsList.Count];
        var roots = new CompilationUnit[pathsList.Count];
        var fileDms = new DiagnosticsManager[pathsList.Count];
        var allInternalDiagnostics = new List<(Diagnostic Diagnostic, SourceText Source, string Path)>();

        Parallel.For(0, pathsList.Count, i =>
        {
            var path = pathsList[i];
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
        });

        // Validate top-level statements and resolve the entry file candidate
        string? effectiveEntryFile = options.EntryFile;
        List<(int Index, CompilationUnit Root, DiagnosticsManager Dm)> filesWithTopLevel = [];

        for (int i = 0; i < roots.Length; i++)
        {
            var unit = roots[i];
            bool? pragmaState = PragmaDirective.GetTopLevelPragmaState(unit.Pragmas);
            bool hasStatements = ContainsTopLevelStatement(unit.Members);
            bool isExplicitEntry = options.EntryFile != null &&
                (string.Equals(pathsList[i], options.EntryFile, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(Path.GetFileName(pathsList[i]), options.EntryFile, StringComparison.OrdinalIgnoreCase));

            if (pragmaState == false)
            {
                unit.EnablesTopLevelStatements = false;
            }
            else if (pragmaState == true)
            {
                unit.EnablesTopLevelStatements = true;
                if (hasStatements)
                    filesWithTopLevel.Add((i, unit, fileDms[i]));
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
                                filesWithTopLevel.Add((i, unit, fileDms[i]));
                        }
                        else
                        {
                            unit.EnablesTopLevelStatements = false;
                            if (hasStatements)
                                filesWithTopLevel.Add((i, unit, fileDms[i]));
                        }
                    }
                    else
                    {
                        if (hasStatements)
                            filesWithTopLevel.Add((i, unit, fileDms[i]));

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
            roots[winnerIdx].EnablesTopLevelStatements = true;
            effectiveEntryFile = pathsList[winnerIdx];
        }
        else if (pathsList.Count == 1 && options.RootDirectory == null && effectiveEntryFile == null)
        {
            effectiveEntryFile = pathsList[0];
            if (roots.Length > 0 && PragmaDirective.GetTopLevelPragmaState(roots[0].Pragmas) != false)
                roots[0].EnablesTopLevelStatements = true;
        }

        if (filesWithTopLevel.Count > 1)
        {
            foreach (var (_, candRoot, candDm) in filesWithTopLevel)
                candDm.ReportMultipleTopLevelSources(GetTopLevelPragmaSpan(candRoot));
        }

        for (int i = 0; i < pathsList.Count; i++)
        {
            foreach (var diag in fileDms[i].Diagnostics)
                allInternalDiagnostics.Add((diag, sourceTexts[i], pathsList[i]));
        }

        var syntaxTree = new SyntaxTree(projectName, roots.ToArray());
        var resolver = new Resolver();

        var referencedContexts = referencedCompilations
            .Where(c => c.Context != null)
            .Select(c => c.Context!)
            .ToList();

        var resolutionDm = new DiagnosticsManager();
        var context = resolver.Resolve(syntaxTree, referencedContexts, diagnostics: resolutionDm, options: options);

        foreach (var diag in resolutionDm.Diagnostics)
        {
            var src = diag.Source;
            string? diagPath = src?.FilePath;
            allInternalDiagnostics.Add((diag, src ?? (pathsList.Count > 0 ? sourceTexts[0] : null!), diagPath ?? (pathsList.Count > 0 ? pathsList[0] : "source.mh")));
        }

        var projected = new List<DiagnosticInfo>(allInternalDiagnostics.Count);
        foreach (var (diag, text, path) in allInternalDiagnostics)
            projected.Add(DiagnosticInfo.FromDiagnostic(diag, text, path));

        return new Compilation(
            projectName,
            [syntaxTree],
            sourceTexts,
            options,
            referencedCompilations,
            context,
            projected);
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
        return result;
    }
}
