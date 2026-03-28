using System;
using System.IO;
using Maho.Diagnostics;
using Maho.Syntax;
using Maho.Text;

namespace Maho;

public static class MahoCompiler
{
    public static CompilerAnalysisResult AnalyzeFile(string filePath, AnalysisOutput output = AnalysisOutput.None)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Source file path cannot be empty.", nameof(filePath));

        string fullPath = Path.GetFullPath(filePath);

        using SourceText text = new(new SourceFile(fullPath));
        return AnalyzeCore(text, fullPath, output);
    }

    public static CompilerAnalysisResult AnalyzeText(string sourceText, AnalysisOutput output = AnalysisOutput.None, string sourcePath = "<memory>")
    {
        if (sourceText is null)
            throw new ArgumentNullException(nameof(sourceText), "Source text cannot be null.");

        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path cannot be empty.", nameof(sourcePath));

        using SourceText text = new(sourceText);
        return AnalyzeCore(text, sourcePath, output);
    }

    private static CompilerAnalysisResult AnalyzeCore(SourceText text, string sourcePath, AnalysisOutput output)
    {
        DiagnosticsManager diagnosticsManager = new();

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

    private static DiagnosticInfo[] CreateDiagnostics(DiagnosticsManager diagnosticsManager, SourceText text)
    {
        DiagnosticInfo[] diagnostics = new DiagnosticInfo[diagnosticsManager.Diagnostics.Count];

        for (int i = 0; i < diagnostics.Length; i++)
        {
            Diagnostic diagnostic = diagnosticsManager.Diagnostics[i];
            diagnostics[i] = new DiagnosticInfo(
                diagnostic.DiagnosticCode,
                diagnostic.Message,
                MapSeverity(diagnostic.Kind),
                CreateSpanInfo(diagnostic.Span, text));
        }

        return diagnostics;
    }

    private static DiagnosticSeverity MapSeverity(DiagnosticKind kind) => kind switch
    {
        DiagnosticKind.Info => DiagnosticSeverity.Info,
        DiagnosticKind.Warning => DiagnosticSeverity.Warning,
        DiagnosticKind.Error => DiagnosticSeverity.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled diagnostic kind.")
    };

    internal static TextSpanInfo CreateSpanInfo(TextSpan span, SourceText text) =>
        new(
            span.Start,
            span.Length,
            span.End,
            new TextLocation(span.GetStartLine(text) + 1, span.GetStartColumn(text) + 1),
            new TextLocation(span.GetEndLine(text) + 1, span.GetEndColumn(text) + 1));
}
