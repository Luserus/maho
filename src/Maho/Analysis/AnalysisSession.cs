using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Diagnostics;
using Maho.Resolution;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Analysis;

/// <summary>
/// Specifies the intended syntax kind of an interactive snippet.
/// </summary>
public enum SnippetKind : byte
{
    Auto,
    Expression,
    Statement,
    Declaration
}

/// <summary>
/// Result of analyzing an isolated interactive snippet within an <see cref="AnalysisSession"/>.
/// </summary>
public sealed class SnippetAnalysisResult
{
    public string SnippetId { get; }
    public string Code { get; }
    public SnippetKind Kind { get; }
    public bool Success { get; }
    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; }
    internal SyntaxTree? SyntaxTree { get; }
    internal ResolutionContext? Context { get; }

    internal SnippetAnalysisResult(
        string snippetId,
        string code,
        SnippetKind kind,
        bool success,
        SyntaxTree? syntaxTree,
        ResolutionContext? context,
        IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        SnippetId = snippetId;
        Code = code;
        Kind = kind;
        Success = success;
        SyntaxTree = syntaxTree;
        Context = context;
        Diagnostics = diagnostics;
    }
}

/// <summary>
/// Stateful session for interactive REPL, CLI interpreter, or incremental compilation.
/// Analyzes isolated blocks of code reusing symbol tables and scopes from previous prompts
/// without needing self-contained, header-full source files.
/// </summary>
public sealed class AnalysisSession
{
    private readonly List<SnippetAnalysisResult> history = [];
    private int snippetCounter;

    public Compilation? RootCompilation { get; }
    internal ResolutionContext CurrentContext { get; private set; }
    public CompilationOptions Options { get; }
    public IReadOnlyList<SnippetAnalysisResult> History => history;
    public int SnippetCount => history.Count;

    public AnalysisSession(Compilation? rootCompilation = null, CompilationOptions? options = null)
    {
        RootCompilation = rootCompilation;
        Options = options ?? rootCompilation?.Options ?? CompilationOptions.Default;

        if (rootCompilation?.Context != null)
        {
            CurrentContext = rootCompilation.Context;
        }
        else
        {
            var emptyTree = new SyntaxTree("<empty>", []);
            CurrentContext = new Resolver().Resolve(emptyTree);
        }
    }

    /// <summary>
    /// Analyzes an isolated block of code against the active symbol table and scopes in this session.
    /// </summary>
    public SnippetAnalysisResult AnalyzeSnippet(string code, SnippetKind kind = SnippetKind.Auto)
    {
        int snippetIndex = ++snippetCounter;
        string snippetId = $"snippet_{snippetIndex}";

        bool injectedPragma = false;
        string effectiveCode = code;

        // Ensure top-level statements and expressions are accepted by the parser
        if (!code.Contains("#pragma toplevel", StringComparison.OrdinalIgnoreCase))
        {
            effectiveCode = "#pragma toplevel enable\n" + code;
            injectedPragma = true;
        }

        var sourceText = new SourceText(effectiveCode);
        var dm = new DiagnosticsManager(sourceText);

        var lexer = new Lexer(sourceText, dm);
        lexer.Lex();

        var parser = new Parser(sourceText, dm);
        var root = parser.Parse(lexer.Tokens);

        var diagnostics = new List<DiagnosticInfo>();

        if (dm.HasErrors)
        {
            foreach (var diag in dm.Diagnostics)
                diagnostics.Add(AdjustDiagnostic(DiagnosticInfo.FromDiagnostic(diag, sourceText, snippetId), injectedPragma));

            return new SnippetAnalysisResult(snippetId, code, kind, false, null, null, diagnostics);
        }

        var syntaxTree = SyntaxTree.CreateSingleRoot(root, snippetId);
        var resolver = new Resolver();
        var newContext = resolver.Resolve(syntaxTree, baseContext: CurrentContext);

        foreach (var diag in dm.Diagnostics)
            diagnostics.Add(AdjustDiagnostic(DiagnosticInfo.FromDiagnostic(diag, sourceText, snippetId), injectedPragma));

        bool hasErrors = diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error ||
            (Options.WarningsAsErrors && d.Severity == DiagnosticSeverity.Warning));

        return new SnippetAnalysisResult(
            snippetId,
            code,
            kind,
            !hasErrors,
            syntaxTree,
            newContext,
            diagnostics);
    }

    /// <summary>
    /// Commits a successfully analyzed snippet into the session's active scope and symbol tables,
    /// enabling subsequent snippets to reference types, functions, and variables from it.
    /// </summary>
    public bool CommitSnippet(SnippetAnalysisResult snippet)
    {
        if (!snippet.Success || snippet.Context is null)
            return false;

        CurrentContext = snippet.Context;
        history.Add(snippet);
        return true;
    }

    private static DiagnosticInfo AdjustDiagnostic(DiagnosticInfo diagnostic, bool injectedPragma)
    {
        if (!injectedPragma)
            return diagnostic;

        // Shift 1-based line numbers down by 1 to hide the injected pragma line from the user
        int adjustedStartLine = Math.Max(1, diagnostic.Span.StartLocation.Line - 1);
        int adjustedEndLine = Math.Max(1, diagnostic.Span.EndLocation.Line - 1);

        var adjustedSpan = diagnostic.Span with
        {
            StartLocation = new TextLocation(adjustedStartLine, diagnostic.Span.StartLocation.Column),
            EndLocation = new TextLocation(adjustedEndLine, diagnostic.Span.EndLocation.Column)
        };

        return diagnostic with { Span = adjustedSpan };
    }
}
