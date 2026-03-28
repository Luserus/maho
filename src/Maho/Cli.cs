using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Maho.Diagnostics;
using Maho.Syntax;
using Maho.Text;

namespace Maho;

internal static class Cli
{
    private const string Reset = "\u001b[0m";
    private const string Dim = "\u001b[2m";
    private const string BrightWhite = "\u001b[97m";
    private const string Red = "\u001b[31m";
    private const string Yellow = "\u001b[33m";
    private const string Cyan = "\u001b[36m";
    private const string BrightBlack = "\u001b[90m";

    private static readonly object statusLock = new();

    [Flags]
    private enum DebugOutput
    {
        None = 0,
        Lexer = 1 << 0,
        Parser = 1 << 1,
    }

    private readonly record struct CliOptions(DebugOutput Output, bool ShowHelp, string? SourcePath);

    private readonly record struct AnalysisResult(string DisplayPath, string Output, bool HasErrors);

    private sealed class AnalysisProgress(int totalFiles)
    {
        private int lexingStarted;
        private int parsingStarted;

        public int TotalFiles { get; } = totalFiles;

        public void ReportLexing(string displayPath) =>
            ReportPhase("lexing", displayPath, Interlocked.Increment(ref lexingStarted), Cyan);

        public void ReportParsing(string displayPath) =>
            ReportPhase("parsing", displayPath, Interlocked.Increment(ref parsingStarted), Yellow);

        private void ReportPhase(string phase, string displayPath, int current, string color)
        {
            lock (statusLock)
            {
                Console.Error.Write(Colorize($"[{current,2}/{TotalFiles,2}]", BrightBlack));
                Console.Error.Write(" ");
                Console.Error.Write(Colorize($"{phase,-7}", color));
                Console.Error.Write(" ");
                Console.Error.WriteLine(Colorize(displayPath, Dim));
            }
        }
    }

    public static int Run(string[] args)
    {
        if (!TryParseArguments(args, out var options, out var errorMessage))
        {
            Console.Error.WriteLine(errorMessage);
            Console.Error.WriteLine();
            PrintUsage(Console.Error);
            return 1;
        }

        if (options.ShowHelp)
        {
            PrintUsage(Console.Out);
            return 0;
        }

        var sourcePath = options.SourcePath is null
            ? GetDefaultTestFilePath()
            : Path.GetFullPath(options.SourcePath);

        if (!TryResolveInputFiles(sourcePath, out var files, out var displayRoot, out var resolutionError))
        {
            Console.Error.WriteLine(resolutionError);
            return 1;
        }

        bool multipleFiles = files.Count > 1;
        AnalysisResult[] results = new AnalysisResult[files.Count];
        AnalysisProgress progress = new(files.Count);

        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, index =>
        {
            string filePath = files[index];
            string displayPath = multipleFiles ? Path.GetRelativePath(displayRoot, filePath) : filePath;
            results[index] = AnalyzeFile(filePath, displayPath, options, includeFileHeader: multipleFiles, progress);
        });

        bool hasErrors = false;

        for (int i = 0; i < results.Length; i++)
        {
            if (!string.IsNullOrEmpty(results[i].Output))
                Console.Out.Write(results[i].Output);

            hasErrors |= results[i].HasErrors;
        }

        return hasErrors ? 1 : 0;
    }

    private static AnalysisResult AnalyzeFile(string filePath, string displayPath, CliOptions options, bool includeFileHeader, AnalysisProgress progress)
    {
        try
        {
            using SourceText text = new(new SourceFile(filePath));
            DiagnosticsManager diagnosticsManager = new();

            progress.ReportLexing(displayPath);
            Lexer lexer = new(text, diagnosticsManager);
            lexer.Lex();

            string? lexerOutput = options.Output.HasFlag(DebugOutput.Lexer) ? lexer.ToString() : null;

            progress.ReportParsing(displayPath);
            Parser parser = new(text, diagnosticsManager);
            parser.Parse(lexer.Tokens);

            string? parserOutput = options.Output.HasFlag(DebugOutput.Parser) ? parser.ToString() : null;
            string diagnosticOutput = RenderDiagnostics(diagnosticsManager, text, displayPath, includeSourcePath: !includeFileHeader);

            StringBuilder sb = new();
            bool wroteAnything = false;

            if (!string.IsNullOrEmpty(lexerOutput) || !string.IsNullOrEmpty(parserOutput) || !string.IsNullOrEmpty(diagnosticOutput))
            {
                if (includeFileHeader)
                {
                    sb.AppendLine();
                    sb.AppendLine(Colorize(filePath, Dim));
                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(lexerOutput))
                {
                    sb.Append(lexerOutput);
                    if (!lexerOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                        sb.AppendLine();

                    wroteAnything = true;
                }

                if (!string.IsNullOrEmpty(parserOutput))
                {
                    if (wroteAnything)
                        sb.AppendLine();

                    sb.Append(parserOutput);
                    if (!parserOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                        sb.AppendLine();

                    wroteAnything = true;
                }

                if (!string.IsNullOrEmpty(diagnosticOutput))
                {
                    if (wroteAnything)
                        sb.AppendLine();

                    sb.Append(diagnosticOutput);
                    wroteAnything = true;
                }

                if (wroteAnything && !sb.ToString().EndsWith(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal))
                    sb.AppendLine();
            }

            return new AnalysisResult(displayPath, sb.ToString(), diagnosticsManager.HasErrors);
        }
        catch (Exception ex)
        {
            StringBuilder sb = new();

            if (includeFileHeader)
            {
                sb.AppendLine();
                sb.AppendLine(Colorize(filePath, Dim));
                sb.AppendLine();
            }

            if (!includeFileHeader)
                sb.AppendLine(Colorize(displayPath, Dim));

            sb.AppendLine($"{Colorize("(internal)", BrightWhite)} {Colorize("error", Red)} {Colorize("MHC9999", Red)}: Unhandled analysis failure.");
            sb.AppendLine();
            sb.AppendLine(ex.Message);
            sb.AppendLine();

            return new AnalysisResult(displayPath, sb.ToString(), HasErrors: true);
        }
    }

    private static bool TryResolveInputFiles(string sourcePath, out List<string> files, out string displayRoot, out string? errorMessage)
    {
        files = [];
        displayRoot = sourcePath;

        if (File.Exists(sourcePath))
        {
            files.Add(sourcePath);
            errorMessage = null;
            return true;
        }

        if (Directory.Exists(sourcePath))
        {
            displayRoot = sourcePath;
            files = [.. Directory.GetFiles(sourcePath, "*.mh", SearchOption.AllDirectories).OrderBy(static path => path, StringComparer.Ordinal)];

            if (files.Count == 0)
            {
                errorMessage = $"No '.mh' files were found in directory: {sourcePath}";
                return false;
            }

            errorMessage = null;
            return true;
        }

        errorMessage = $"Input path not found: {sourcePath}";
        return false;
    }

    private static bool TryParseArguments(string[] args, out CliOptions options, out string? errorMessage)
    {
        DebugOutput output = DebugOutput.None;
        bool showHelp = false;
        string? sourcePath = null;

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "-l":
                case "--lex":
                    output |= DebugOutput.Lexer;
                    break;

                case "-p":
                case "--parse":
                    output |= DebugOutput.Parser;
                    break;

                case "-a":
                case "--all":
                    output |= DebugOutput.Lexer | DebugOutput.Parser;
                    break;

                case "-h":
                case "--help":
                    showHelp = true;
                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        options = default;
                        errorMessage = $"Unknown option '{arg}'.";
                        return false;
                    }

                    if (sourcePath is not null)
                    {
                        options = default;
                        errorMessage = "Only one source file or directory path can be provided.";
                        return false;
                    }

                    sourcePath = arg;
                    break;
            }
        }

        options = new CliOptions(output, showHelp, sourcePath);
        errorMessage = null;
        return true;
    }

    private static string GetDefaultTestFilePath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Test.mh"));

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: Maho [options] [source-path]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  -l, --lex     Print the lexer token stream.");
        writer.WriteLine("  -p, --parse   Print the parser syntax tree.");
        writer.WriteLine("  -a, --all     Print both debug views.");
        writer.WriteLine("  -h, --help    Show this help text.");
        writer.WriteLine();
        writer.WriteLine("The source path may be a single '.mh' file or a directory.");
        writer.WriteLine("Directory inputs analyze every '.mh' file recursively.");
        writer.WriteLine("When no source path is provided, the local Test.mh file is used.");
        writer.WriteLine("When no debug flags are provided, nothing is printed unless diagnostics are reported.");
    }

    private static string RenderDiagnostics(DiagnosticsManager diagnosticsManager, SourceText text, string sourcePath, bool includeSourcePath)
    {
        if (diagnosticsManager.Diagnostics.Count == 0)
            return string.Empty;

        using StringWriter writer = new();
        PrintDiagnostics(writer, diagnosticsManager, text, sourcePath, includeSourcePath);
        return writer.ToString();
    }

    private static void PrintDiagnostics(TextWriter writer, DiagnosticsManager diagnosticsManager, SourceText text, string sourcePath, bool includeSourcePath)
    {
        List<(Diagnostic Diagnostic, int Index, int Line)> orderedDiagnostics = [];

        for (int i = 0; i < diagnosticsManager.Diagnostics.Count; i++)
        {
            var diagnostic = diagnosticsManager.Diagnostics[i];
            orderedDiagnostics.Add((diagnostic, i, diagnostic.Span.GetStartLine(text)));
        }

        orderedDiagnostics.Sort(static (left, right) =>
        {
            int byLine = left.Line.CompareTo(right.Line);

            if (byLine != 0)
                return byLine;

            return left.Index.CompareTo(right.Index);
        });

        foreach (var entry in orderedDiagnostics)
            PrintDiagnostic(writer, entry.Diagnostic, text, sourcePath, includeSourcePath);
    }

    private static void PrintDiagnostic(TextWriter writer, Diagnostic diagnostic, SourceText text, string sourcePath, bool includeSourcePath)
    {
        int startLineIndex = diagnostic.Span.GetStartLine(text);
        int endLineIndex = diagnostic.Span.GetEndLine(text);
        int startLine = startLineIndex + 1;
        int startColumn = diagnostic.Span.GetStartColumn(text) + 1;
        int endLine = endLineIndex + 1;
        int endColumn = diagnostic.Span.GetEndColumn(text) + 1;
        string severity = diagnostic.Kind.ToString().ToLowerInvariant();
        string accent = GetDiagnosticColor(diagnostic.Kind);

        if (includeSourcePath)
            writer.WriteLine(Colorize(sourcePath, Dim));

        writer.Write(Colorize($"({startLine}, {startColumn}) ", BrightWhite));
        writer.Write(Colorize($"{severity} ", accent));
        writer.Write(Colorize(diagnostic.DiagnosticCode, accent));
        writer.Write(": ");
        writer.WriteLine(diagnostic.Message);
        writer.WriteLine();

        PrintDiagnosticContext(writer, diagnostic, text, startLineIndex, endLineIndex, accent, endLine, endColumn);
        writer.WriteLine();
    }

    private static void PrintDiagnosticContext(TextWriter writer, Diagnostic diagnostic, SourceText text, int startLineIndex, int endLineIndex, string accent, int endLineNumber, int endColumn)
    {
        int maxContextLines = 3;
        int lastLineIndex = Math.Min(endLineIndex, startLineIndex + maxContextLines - 1);
        int lineNumberWidth = Math.Max(2, (lastLineIndex + 1).ToString().Length);
        string? tipIndent = null;

        for (int lineIndex = startLineIndex; lineIndex <= lastLineIndex; lineIndex++)
        {
            var line = text.Lines[lineIndex];
            string lineText = line.ToString();
            string displayedLineText = lineText.Replace("\t", "    ");
            int lineNumber = lineIndex + 1;

            writer.Write(Colorize($"{lineNumber.ToString().PadLeft(lineNumberWidth)} | ", Dim));
            writer.WriteLine(displayedLineText);

            int underlineStart = GetUnderlineStart(diagnostic.Span, line);
            int underlineWidth = GetUnderlineWidth(diagnostic.Span, line, lineIndex == endLineIndex && diagnostic.Span.Length == 0);
            string markerIndent = ExpandIndentation(lineText, underlineStart);
            string marker = new('^', Math.Max(1, underlineWidth));

            writer.Write(Colorize($"{new string(' ', lineNumberWidth)} | ", Dim));
            writer.Write(markerIndent);
            writer.Write(Colorize(marker, accent));
            writer.WriteLine();

            if (lineIndex == startLineIndex)
                tipIndent = markerIndent;
        }

        if (tipIndent is not null)
            PrintDiagnosticTip(writer, diagnostic, lineNumberWidth, tipIndent);

        if (lastLineIndex < endLineIndex)
        {
            writer.Write(Colorize($"{new string(' ', lineNumberWidth)} | ", Dim));
            writer.WriteLine(Colorize("...", Dim));
            writer.Write(Colorize($"{new string(' ', lineNumberWidth)} | ", Dim));
            writer.WriteLine(Colorize($"continues through line {endLineNumber}, column {endColumn}", Dim));
        }
    }

    private static void PrintDiagnosticTip(TextWriter writer, Diagnostic diagnostic, int lineNumberWidth, string indent)
    {
        string tipColor = Colorize("tip", Cyan);
        string tipText = GetDiagnosticTip(diagnostic);

        writer.Write(Colorize($"{new string(' ', lineNumberWidth)} | ", Dim));
        writer.Write(indent);
        writer.Write(Colorize("└─ ", BrightBlack));
        writer.Write(tipColor);
        writer.Write(": ");
        writer.WriteLine(Colorize(tipText, Dim));
    }

    private static int GetUnderlineStart(TextSpan span, TextLine line)
    {
        if (span.Start <= line.Start)
            return 0;

        return Math.Min(span.Start - line.Start, line.Length);
    }

    private static int GetUnderlineWidth(TextSpan span, TextLine line, bool zeroLengthAtEndLine)
    {
        int highlightStart = Math.Max(span.Start, line.Start);
        int highlightEnd = Math.Min(span.End, line.End);
        int width = highlightEnd - highlightStart;

        if (width > 0)
            return GetExpandedWidth(line.ToString(), highlightStart - line.Start, width);

        if (zeroLengthAtEndLine)
            return 1;

        return 1;
    }

    private static string ExpandIndentation(string text, int count)
    {
        if (count <= 0)
            return string.Empty;

        int safeCount = Math.Min(count, text.Length);
        var prefix = text[..safeCount];
        char[] spaces = new char[prefix.Length];

        for (int i = 0; i < prefix.Length; i++)
            spaces[i] = prefix[i] == '\t' ? '\t' : ' ';

        return new string(spaces).Replace("\t", "    ");
    }

    private static int GetExpandedWidth(string text, int start, int width)
    {
        if (width <= 0)
            return 1;

        int safeStart = Math.Min(start, text.Length);
        int safeWidth = Math.Min(width, text.Length - safeStart);

        if (safeWidth <= 0)
            return 1;

        return text.Substring(safeStart, safeWidth).Replace("\t", "    ").Length;
    }

    private static string GetDiagnosticTip(Diagnostic diagnostic) => diagnostic.DiagnosticCode switch
    {
        "MHC0001" => "Remove or replace this token.",
        "MHC0002" => "Add the closing double quote before the line ends.",
        "MHC0003" => "Add the closing single quote before the line ends.",
        "MHC0004" => "Add at least one character between the quotes.",
        "MHC1001" => "Insert the missing token here.",
        "MHC1002" => "Add an expression here.",
        "MHC1003" => "Add an identifier here.",
        "MHC1004" => "Add a type here.",
        _ => "Check this location."
    };

    private static string GetDiagnosticColor(DiagnosticKind kind) => kind switch
    {
        DiagnosticKind.Error => Red,
        DiagnosticKind.Warning => Yellow,
        _ => Cyan
    };

    private static string Colorize(string value, string color)
    {
        if (!ShouldUseColor())
            return value;

        return $"{color}{value}{Reset}";
    }

    private static bool ShouldUseColor()
    {
        if (Console.IsOutputRedirected)
            return false;

        if (Environment.GetEnvironmentVariable("NO_COLOR") is not null)
            return false;

        var term = Environment.GetEnvironmentVariable("TERM");
        return !string.IsNullOrEmpty(term) && !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);
    }
}
