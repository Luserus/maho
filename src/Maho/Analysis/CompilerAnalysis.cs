using System;
using System.IO;
using Maho.Diagnostics;
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
    /// Analyzes a source file from disk and returns diagnostics plus any requested debug artifacts.
    /// The file path is normalized first so downstream consumers see a stable source identity.
    /// </summary>
    public static CompilerAnalysisResult AnalyzeFile(string filePath, AnalysisOutput output = AnalysisOutput.None)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Source file path cannot be empty.", nameof(filePath));

        string fullPath = Path.GetFullPath(filePath);

        using SourceText text = new(new SourceFile(fullPath));
        return AnalyzeCore(text, fullPath, output);
    }

    /// <summary>
    /// Analyzes already-loaded source text, which is useful for tests, editor integrations, and
    /// other callers that do not want the compiler API to own file I/O.
    /// </summary>
    public static CompilerAnalysisResult AnalyzeText(string sourceText, AnalysisOutput output = AnalysisOutput.None, string sourcePath = "<memory>")
    {
        if (sourceText is null)
            throw new ArgumentNullException(nameof(sourceText), "Source text cannot be null.");

        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path cannot be empty.", nameof(sourcePath));

        using SourceText text = new(sourceText);
        return AnalyzeCore(text, sourcePath, output);
    }

    /// <summary>
    /// Runs the shared front-end pipeline against a prepared <see cref="SourceText"/> instance.
    /// Lexer and parser share one diagnostics manager so callers receive a single coherent report.
    /// </summary>
    private static CompilerAnalysisResult AnalyzeCore(SourceText text, string sourcePath, AnalysisOutput output)
    {
        DiagnosticsManager diagnosticsManager = new();

        // Lexer and parser intentionally share one diagnostics sink so callers receive a single
        // ordered report for the entire front-end pass.
        Lexer lexer = new(text, diagnosticsManager);
        lexer.Lex();

        Parser parser = new(text, diagnosticsManager);
        parser.Parse(lexer.Tokens);

        DiagnosticInfo[] diagnostics = CreateDiagnostics(diagnosticsManager, text);

        return new CompilerAnalysisResult(
            sourcePath,
            output.HasFlag(AnalysisOutput.Lexer) ? lexer.ToJson() : null,
            output.HasFlag(AnalysisOutput.Parser) ? parser.ToJson() : null,
            diagnostics,
            DebugJson.Serialize(diagnostics));
    }

    /// <summary>
    /// Projects internal diagnostics into the public result model, enriching raw spans with
    /// line/column information so consumers do not need the original source buffer.
    /// </summary>
    private static DiagnosticInfo[] CreateDiagnostics(DiagnosticsManager diagnosticsManager, SourceText text)
    {
        DiagnosticInfo[] diagnostics = new DiagnosticInfo[diagnosticsManager.Diagnostics.Count];

        for (int i = 0; i < diagnostics.Length; i++)
        {
            Diagnostic diagnostic = diagnosticsManager.Diagnostics[i];
            // This is the only place internal diagnostics become part of the public API contract, so
            // severity and span projection stay centralized here.
            diagnostics[i] = new DiagnosticInfo(
                diagnostic.DiagnosticCode,
                diagnostic.Message,
                MapSeverity(diagnostic.Kind),
                CreateSpanInfo(diagnostic.Span, text));
        }

        return diagnostics;
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
}
