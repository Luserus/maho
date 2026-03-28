using System;
using System.Collections.Generic;
using System.IO;
using Maho.Diagnostics;
using Maho.Syntax;
using Maho.Text;

namespace Maho;

[Flags]
public enum AnalysisOutput
{
    None = 0,
    Lexer = 1 << 0,
    Parser = 1 << 1
}

public enum DiagnosticSeverity : byte
{
    Info,
    Warning,
    Error
}

public sealed record TextLocation(int Line, int Column);

public sealed record TextSpanInfo(int Start, int Length, int End, TextLocation StartLocation, TextLocation EndLocation);

public sealed record DiagnosticInfo(string Code, string Message, DiagnosticSeverity Severity, TextSpanInfo Span);

public sealed record CompilerAnalysisResult(
    string SourcePath,
    string? LexerJson,
    string? ParserJson,
    IReadOnlyList<DiagnosticInfo> Diagnostics)
{
    public bool HasErrors
    {
        get
        {
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                if (Diagnostics[i].Severity is DiagnosticSeverity.Error)
                    return true;
            }

            return false;
        }
    }
}

public static class MahoCompiler
{
    public static CompilerAnalysisResult AnalyzeFile(string filePath, AnalysisOutput output = AnalysisOutput.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string fullPath = Path.GetFullPath(filePath);

        using SourceText text = new(new SourceFile(fullPath));
        return AnalyzeCore(text, fullPath, output);
    }

    public static CompilerAnalysisResult AnalyzeText(string sourceText, AnalysisOutput output = AnalysisOutput.None, string sourcePath = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

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

        return new CompilerAnalysisResult(
            sourcePath,
            output.HasFlag(AnalysisOutput.Lexer) ? lexer.ToString() : null,
            output.HasFlag(AnalysisOutput.Parser) ? parser.ToString() : null,
            CreateDiagnostics(diagnosticsManager, text));
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
        _ => DiagnosticSeverity.Error
    };

    internal static TextSpanInfo CreateSpanInfo(TextSpan span, SourceText text) =>
        new(
            span.Start,
            span.Length,
            span.End,
            new TextLocation(span.GetStartLine(text) + 1, span.GetStartColumn(text) + 1),
            new TextLocation(span.GetEndLine(text) + 1, span.GetEndColumn(text) + 1));
}
