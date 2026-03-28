using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Maho.Cli;

internal static class CommandLine
{
    private const string Reset = "\u001b[0m";
    private const string Dim = "\u001b[2m";
    private const string BrightWhite = "\u001b[97m";
    private const string Red = "\u001b[31m";
    private const string Yellow = "\u001b[33m";
    private const string Cyan = "\u001b[36m";
    private const string BrightBlack = "\u001b[90m";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private static readonly object statusLock = new();

    private readonly record struct CliOptions(AnalysisOutput Output, bool ShowHelp, bool ShowProgress, string? EmitPath, string? SourcePath);

    private readonly record struct FileResult(
        string FilePath,
        string DisplayPath,
        CompilerAnalysisResult? Analysis,
        string DebugOutput,
        string DiagnosticOutput,
        string? AnalysisError,
        bool HasErrors);

    private readonly struct SourceLine
    {
        public string Text { get; }
        public int Start { get; }
        public int End { get; }

        public SourceLine(string text, int start, int end)
        {
            Text = text;
            Start = start;
            End = end;
        }
    }

    private sealed class SourceBuffer
    {
        private readonly SourceLine[] lines;

        public SourceLine[] Lines => lines;

        private SourceBuffer(string text)
        {
            lines = ParseLines(text);
        }

        public static SourceBuffer Load(string filePath) => new(File.ReadAllText(filePath));

        public int GetLineIndex(int position)
        {
            int lower = 0;
            int upper = lines.Length - 1;

            while (lower <= upper)
            {
                int index = lower + ((upper - lower) >> 1);
                int start = lines[index].Start;

                if (position == start)
                    return index;

                if (position < start)
                    upper = index - 1;
                else
                    lower = index + 1;
            }

            return Math.Max(0, lower - 1);
        }

        private static SourceLine[] ParseLines(string text)
        {
            List<SourceLine> parsedLines = [];
            int position = 0;
            int lineStart = 0;

            while (position < text.Length)
            {
                int breakWidth = GetLineBreakWidth(text, position);

                if (breakWidth == 0)
                {
                    position++;
                    continue;
                }

                AddLine(parsedLines, text, lineStart, position);
                position += breakWidth;
                lineStart = position;
            }

            if (position >= lineStart)
                AddLine(parsedLines, text, lineStart, position);

            return [.. parsedLines];
        }

        private static void AddLine(List<SourceLine> lines, string text, int start, int end) =>
            lines.Add(new SourceLine(text.Substring(start, end - start), start, end));

        private static int GetLineBreakWidth(string text, int position)
        {
            char ch = text[position];
            char next = position + 1 < text.Length ? text[position + 1] : '\0';

            if (ch == '\r' && next == '\n')
                return 2;

            if (ch == '\r' || ch == '\n')
                return 1;

            return 0;
        }
    }

    private sealed class AnalysisProgress(int totalFiles)
    {
        private int analyzedFiles;

        public int TotalFiles { get; } = totalFiles;

        public void ReportAnalyzing(string displayPath)
        {
            int current = Interlocked.Increment(ref analyzedFiles);

            lock (statusLock)
            {
                Console.Error.Write(Colorize($"[{current,2}/{TotalFiles,2}]", BrightBlack));
                Console.Error.Write(" ");
                Console.Error.Write(Colorize("analyzing", Cyan));
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

        string sourcePath = options.SourcePath is null
            ? GetDefaultSourcePath()
            : Path.GetFullPath(options.SourcePath);

        if (!TryResolveInputFiles(sourcePath, out var files, out var displayRoot, out var resolutionError))
        {
            Console.Error.WriteLine(resolutionError);
            return 1;
        }

        bool multipleFiles = files.Count > 1;
        bool emitPrettyOutput = options.EmitPath is null;
        FileResult[] results = new FileResult[files.Count];
        AnalysisProgress? progress = options.ShowProgress ? new AnalysisProgress(files.Count) : null;

        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, index =>
        {
            string filePath = files[index];
            string displayPath = multipleFiles ? Path.GetRelativePath(displayRoot, filePath) : filePath;
            results[index] = AnalyzeFile(filePath, displayPath, options.Output, includeFileHeader: multipleFiles, emitPrettyOutput, progress);
        });

        bool writeFailed = false;
        string? completionMessage = null;

        if (options.EmitPath is not null)
        {
            string debugJson = BuildDebugOutput(sourcePath, results);

            if (!TryWriteOutputFile(options.EmitPath, debugJson, out var fullOutputPath, out var writeError))
            {
                Console.Error.WriteLine(writeError);
                writeFailed = true;
            }
            else
            {
                completionMessage = $"Finished the work. Stored JSON output at {fullOutputPath}.";
            }
        }

        bool hasErrors = false;

        for (int i = 0; i < results.Length; i++)
        {
            if (!string.IsNullOrEmpty(results[i].DebugOutput))
                Console.Out.Write(results[i].DebugOutput);

            if (!string.IsNullOrEmpty(results[i].DiagnosticOutput))
                Console.Error.Write(results[i].DiagnosticOutput);

            hasErrors |= results[i].HasErrors;
        }

        if (!writeFailed && completionMessage is not null)
            WriteStatus(completionMessage);

        return hasErrors || writeFailed ? 1 : 0;
    }

    private static FileResult AnalyzeFile(string filePath, string displayPath, AnalysisOutput output, bool includeFileHeader, bool emitPrettyOutput, AnalysisProgress? progress)
    {
        try
        {
            progress?.ReportAnalyzing(displayPath);

            CompilerAnalysisResult analysis = MahoCompiler.AnalyzeFile(filePath, output);

            string debugOutput = emitPrettyOutput
                ? RenderDebugOutput(analysis, displayPath, includeFileHeader)
                : string.Empty;
            string diagnosticOutput = RenderDiagnostics(analysis, displayPath);
            return new FileResult(filePath, displayPath, analysis, debugOutput, diagnosticOutput, null, analysis.HasErrors);
        }
        catch (Exception ex)
        {
            return new FileResult(
                filePath,
                displayPath,
                null,
                string.Empty,
                RenderInternalFailure(displayPath, ex),
                ex.Message,
                HasErrors: true);
        }
    }

    private static string BuildDebugOutput(string inputPath, IReadOnlyList<FileResult> results)
    {
        JsonArray fileArray = [];

        for (int i = 0; i < results.Count; i++)
        {
            FileResult result = results[i];
            JsonObject fileObject = new()
            {
                ["filePath"] = result.FilePath,
                ["displayPath"] = result.DisplayPath
            };

            if (result.Analysis?.LexerJson is string lexerJson)
                fileObject["lexer"] = JsonNode.Parse(lexerJson);

            if (result.Analysis?.ParserJson is string parserJson)
                fileObject["parser"] = JsonNode.Parse(parserJson);

            if (!string.IsNullOrEmpty(result.AnalysisError))
                fileObject["analysisError"] = result.AnalysisError;

            fileArray.Add(fileObject);
        }

        JsonObject output = new()
        {
            ["inputPath"] = inputPath,
            ["files"] = fileArray
        };

        return output.ToJsonString(JsonOptions);
    }

    private static bool TryWriteOutputFile(string outputPath, string content, out string? fullPath, out string? errorMessage)
    {
        try
        {
            fullPath = Path.GetFullPath(outputPath);
            string? directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(fullPath, content);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            fullPath = null;
            errorMessage = $"Failed to write JSON output file '{outputPath}': {ex.Message}";
            return false;
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
        AnalysisOutput output = AnalysisOutput.None;
        bool showHelp = false;
        bool showProgress = false;
        string? emitPath = null;
        string? sourcePath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg)
            {
                case "-l":
                case "--lex":
                    output |= AnalysisOutput.Lexer;
                    break;

                case "-p":
                case "--parse":
                    output |= AnalysisOutput.Parser;
                    break;

                case "-a":
                case "--all":
                    output |= AnalysisOutput.Lexer | AnalysisOutput.Parser;
                    break;

                case "--progress":
                    showProgress = true;
                    break;

                case "-o":
                case "--output":
                    if (!TryReadArgumentValue(args, ref i, arg, out emitPath, out errorMessage))
                    {
                        options = default;
                        return false;
                    }

                    break;

                case "-h":
                case "--help":
                    showHelp = true;
                    break;

                default:
                    if (arg.Length > 0 && arg[0] == '-')
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

        if (emitPath is not null && output is AnalysisOutput.None)
        {
            options = default;
            errorMessage = "The output path requires --lex, --parse, or --all.";
            return false;
        }

        options = new CliOptions(output, showHelp, showProgress, emitPath, sourcePath);
        errorMessage = null;
        return true;
    }

    private static bool TryReadArgumentValue(string[] args, ref int index, string optionName, out string? value, out string? errorMessage)
    {
        if (index + 1 >= args.Length)
        {
            value = null;
            errorMessage = $"Option '{optionName}' requires a path argument.";
            return false;
        }

        value = args[++index];
        errorMessage = null;
        return true;
    }

    private static string GetDefaultSourcePath() => Path.GetFullPath(Directory.GetCurrentDirectory());

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: Maho.Cli [options] [source-path]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  -l, --lex       Print the lexer token stream.");
        writer.WriteLine("  -p, --parse     Print the parser syntax tree.");
        writer.WriteLine("  -a, --all       Print both debug views.");
        writer.WriteLine("      --progress  Show per-file analysis progress on stderr.");
        writer.WriteLine("  -o, --output    Write the requested debug views as JSON to the specified file.");
        writer.WriteLine("  -h, --help      Show this help text.");
        writer.WriteLine();
        writer.WriteLine("The source path may be a single '.mh' file or a directory.");
        writer.WriteLine("Directory inputs analyze every '.mh' file recursively.");
        writer.WriteLine("When no source path is provided, the current working directory is scanned recursively for '.mh' files.");
        writer.WriteLine("Debug views are printed to stdout when --output is not provided.");
        writer.WriteLine("When --output is provided, JSON is written to the file and diagnostics/progress stay on stderr.");
    }

    private static string RenderDebugOutput(CompilerAnalysisResult analysis, string displayPath, bool includeFileHeader)
    {
        string? lexerOutput = analysis.LexerOutput;
        string? parserOutput = analysis.ParserOutput;

        if (string.IsNullOrEmpty(lexerOutput) && string.IsNullOrEmpty(parserOutput))
            return string.Empty;

        StringBuilder sb = new();
        bool wroteAnything = false;

        if (includeFileHeader)
        {
            sb.AppendLine();
            sb.AppendLine(Colorize(displayPath, Dim));
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

        if (wroteAnything && !sb.ToString().EndsWith(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal))
            sb.AppendLine();

        return sb.ToString();
    }

    private static string RenderDiagnostics(CompilerAnalysisResult analysis, string displayPath)
    {
        if (analysis.Diagnostics.Count == 0)
            return string.Empty;

        try
        {
            SourceBuffer buffer = SourceBuffer.Load(analysis.SourcePath);
            using StringWriter writer = new();

            writer.WriteLine(Colorize(displayPath, Dim));
            writer.WriteLine();

            PrintDiagnostics(writer, analysis.Diagnostics, buffer);
            return writer.ToString();
        }
        catch (Exception ex)
        {
            using StringWriter writer = new();
            writer.WriteLine(Colorize(displayPath, Dim));
            writer.WriteLine();

            for (int i = 0; i < analysis.Diagnostics.Count; i++)
                PrintDiagnosticSummary(writer, analysis.Diagnostics[i]);

            writer.WriteLine(Colorize($"tip: failed to load source context: {ex.Message}", BrightBlack));
            writer.WriteLine();
            return writer.ToString();
        }
    }

    private static void PrintDiagnostics(TextWriter writer, IReadOnlyList<DiagnosticInfo> diagnostics, SourceBuffer buffer)
    {
        List<(DiagnosticInfo Diagnostic, int Index)> orderedDiagnostics = [];

        for (int i = 0; i < diagnostics.Count; i++)
            orderedDiagnostics.Add((diagnostics[i], i));

        orderedDiagnostics.Sort(static (left, right) =>
        {
            int byLine = left.Diagnostic.Span.StartLocation.Line.CompareTo(right.Diagnostic.Span.StartLocation.Line);

            if (byLine != 0)
                return byLine;

            int byColumn = left.Diagnostic.Span.StartLocation.Column.CompareTo(right.Diagnostic.Span.StartLocation.Column);

            if (byColumn != 0)
                return byColumn;

            return left.Index.CompareTo(right.Index);
        });

        for (int i = 0; i < orderedDiagnostics.Count; i++)
            PrintDiagnostic(writer, orderedDiagnostics[i].Diagnostic, buffer);
    }

    private static void PrintDiagnostic(TextWriter writer, DiagnosticInfo diagnostic, SourceBuffer buffer)
    {
        PrintDiagnosticSummary(writer, diagnostic);
        writer.WriteLine();

        int startLineIndex = buffer.GetLineIndex(diagnostic.Span.Start);
        int endLineIndex = buffer.GetLineIndex(diagnostic.Span.End);

        PrintDiagnosticContext(
            writer,
            diagnostic,
            buffer,
            startLineIndex,
            endLineIndex,
            GetDiagnosticColor(diagnostic.Severity),
            diagnostic.Span.EndLocation.Line,
            diagnostic.Span.EndLocation.Column);

        writer.WriteLine();
    }

    private static void PrintDiagnosticSummary(TextWriter writer, DiagnosticInfo diagnostic)
    {
        string severity = diagnostic.Severity.ToString().ToLowerInvariant();
        string accent = GetDiagnosticColor(diagnostic.Severity);

        writer.Write(Colorize($"({diagnostic.Span.StartLocation.Line}, {diagnostic.Span.StartLocation.Column}) ", BrightWhite));
        writer.Write(Colorize($"{severity} ", accent));
        writer.Write(Colorize(diagnostic.Code, accent));
        writer.Write(": ");
        writer.WriteLine(diagnostic.Message);
    }

    private static void PrintDiagnosticContext(TextWriter writer, DiagnosticInfo diagnostic, SourceBuffer buffer, int startLineIndex, int endLineIndex, string accent, int endLineNumber, int endColumn)
    {
        int maxContextLines = 3;
        int lastLineIndex = Math.Min(endLineIndex, startLineIndex + maxContextLines - 1);
        int lineNumberWidth = Math.Max(2, (lastLineIndex + 1).ToString().Length);
        string? tipIndent = null;

        for (int lineIndex = startLineIndex; lineIndex <= lastLineIndex; lineIndex++)
        {
            SourceLine line = buffer.Lines[lineIndex];
            int lineNumber = lineIndex + 1;

            writer.Write(Colorize($"{lineNumber.ToString().PadLeft(lineNumberWidth)} | ", Dim));
            writer.WriteLine(line.Text.Replace("\t", "    "));

            int underlineStart = GetUnderlineStart(diagnostic.Span, line);
            int underlineWidth = GetUnderlineWidth(diagnostic.Span, line, lineIndex == endLineIndex && diagnostic.Span.Length == 0);
            string markerIndent = ExpandIndentation(line.Text, underlineStart);
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

    private static void PrintDiagnosticTip(TextWriter writer, DiagnosticInfo diagnostic, int lineNumberWidth, string indent)
    {
        writer.Write(Colorize($"{new string(' ', lineNumberWidth)} | ", Dim));
        writer.Write(indent);
        writer.Write(Colorize("└─ ", BrightBlack));
        writer.Write(Colorize("tip", Cyan));
        writer.Write(": ");
        writer.WriteLine(Colorize(GetDiagnosticTip(diagnostic), Dim));
    }

    private static int GetUnderlineStart(TextSpanInfo span, SourceLine line)
    {
        if (span.Start <= line.Start)
            return 0;

        return Math.Min(span.Start - line.Start, line.Text.Length);
    }

    private static int GetUnderlineWidth(TextSpanInfo span, SourceLine line, bool zeroLengthAtEndLine)
    {
        int highlightStart = Math.Max(span.Start, line.Start);
        int highlightEnd = Math.Min(span.End, line.End);
        int width = highlightEnd - highlightStart;

        if (width > 0)
            return GetExpandedWidth(line.Text, highlightStart - line.Start, width);

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

    private static string GetDiagnosticTip(DiagnosticInfo diagnostic) => diagnostic.Code switch
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

    private static string RenderInternalFailure(string displayPath, Exception ex)
    {
        using StringWriter writer = new();
        writer.WriteLine(Colorize(displayPath, Dim));
        writer.WriteLine();
        writer.WriteLine($"{Colorize("(internal)", BrightWhite)} {Colorize("error", Red)} {Colorize("MHC9999", Red)}: Unhandled analysis failure.");
        writer.WriteLine();
        writer.WriteLine(ex.Message);
        writer.WriteLine();
        return writer.ToString();
    }

    private static string GetDiagnosticColor(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => Red,
        DiagnosticSeverity.Warning => Yellow,
        _ => Cyan
    };

    private static void WriteStatus(string message)
    {
        lock (statusLock)
            Console.Error.WriteLine(Colorize(message, BrightBlack));
    }

    private static string Colorize(string value, string color)
    {
        if (!ShouldUseColor())
            return value;

        return $"{color}{value}{Reset}";
    }

    private static bool ShouldUseColor()
    {
        if (Console.IsErrorRedirected)
            return false;

        if (Environment.GetEnvironmentVariable("NO_COLOR") is not null)
            return false;

        string? term = Environment.GetEnvironmentVariable("TERM");
        return !string.IsNullOrEmpty(term) && !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);
    }
}
