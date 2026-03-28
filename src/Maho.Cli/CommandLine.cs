using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Maho.Cli;

internal static class CommandLine
{
    private const string Reset = "\u001b[0m";
    private const string Dim = "\u001b[2m";
    private const string Cyan = "\u001b[36m";
    private const string BrightBlack = "\u001b[90m";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private static readonly object statusLock = new();
    private static bool pendingStatusSeparator;

    private enum DiagnosticOutputFormat : byte
    {
        Text,
        Json
    }

    private readonly record struct CliOptions(
        AnalysisOutput Output,
        bool ShowHelp,
        bool ShowProgress,
        string? EmitPath,
        string? DiagnosticsEmitPath,
        DiagnosticOutputFormat DiagnosticsFormat,
        string? SourcePath);

    private readonly record struct FileResult(
        string FilePath,
        string DisplayPath,
        CompilerAnalysisResult? Analysis,
        string? AnalysisError,
        bool IsInternalError,
        bool HasErrors);

    private sealed class AnalysisProgress(int totalFiles)
    {
        private int analyzedFiles;

        public int TotalFiles { get; } = totalFiles;

        public void ReportAnalyzing(string displayPath)
        {
            lock (statusLock)
            {
                int current = ++analyzedFiles;
                Console.Error.Write(Colorize($"[{current}/{TotalFiles}]", BrightBlack));
                Console.Error.Write(" ");
                Console.Error.Write(Colorize("analyzing", Cyan));
                Console.Error.Write(" ");
                Console.Error.WriteLine(Colorize(displayPath, Dim));
                pendingStatusSeparator = true;
            }
        }
    }

    public static int Run(string[] args)
    {
        if (!TryParseArguments(args, out CliOptions options, out string? errorMessage))
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

        if (options.DiagnosticsEmitPath is null &&
            options.DiagnosticsFormat is DiagnosticOutputFormat.Json &&
            options.EmitPath is null &&
            options.Output is not AnalysisOutput.None)
        {
            Console.Error.WriteLine("JSON diagnostics cannot be printed to stdout while debug views are also written to stdout. Use --diagnostics-output or --output.");
            return 1;
        }

        if (!TryGetSourcePath(options.SourcePath, out string sourcePath, out string? sourcePathError))
        {
            Console.Error.WriteLine(sourcePathError);
            return 1;
        }

        if (!TryResolveInputFiles(sourcePath, out List<string> files, out string displayRoot, out string? resolutionError))
        {
            Console.Error.WriteLine(resolutionError);
            return 1;
        }

        bool multipleFiles = files.Count > 1;
        FileResult[] results = new FileResult[files.Count];
        AnalysisProgress? progress = options.ShowProgress ? new AnalysisProgress(files.Count) : null;

        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, index =>
        {
            string filePath = files[index];
            string displayPath = multipleFiles ? Path.GetRelativePath(displayRoot, filePath) : filePath;
            results[index] = AnalyzeFile(filePath, displayPath, options.Output, progress);
        });

        bool hasErrors = false;

        for (int i = 0; i < results.Length; i++)
            hasErrors |= results[i].HasErrors;

        bool writeFailed = false;
        List<string> completionMessages = [];

        if (options.EmitPath is not null)
        {
            string debugJson = BuildDebugOutput(sourcePath, results);

            if (!TryWriteOutputFile(options.EmitPath, debugJson, out string? fullOutputPath, out string? writeError))
            {
                Console.Error.WriteLine(writeError);
                writeFailed = true;
            }
            else
            {
                completionMessages.Add($"Stored JSON output at {fullOutputPath}.");
            }
        }
        else
        {
            WriteStatusSeparatorIfNeeded();

            for (int i = 0; i < results.Length; i++)
            {
                CompilerAnalysisResult? analysis = results[i].Analysis;

                if (analysis is null)
                    continue;

                string debugOutput = SerializedAnalysisRenderer.RenderDebugOutput(analysis, results[i].DisplayPath, includeFileHeader: multipleFiles, useColor: true);

                if (!string.IsNullOrEmpty(debugOutput))
                    Console.Out.Write(debugOutput);
            }
        }

        string diagnosticsOutput = options.DiagnosticsFormat switch
        {
            DiagnosticOutputFormat.Text => BuildDiagnosticsTextOutput(results, useColor: options.DiagnosticsEmitPath is null),
            DiagnosticOutputFormat.Json => BuildDiagnosticsJsonOutput(sourcePath, results),
            _ => throw new ArgumentOutOfRangeException(nameof(options.DiagnosticsFormat), options.DiagnosticsFormat, "Unhandled diagnostics output format.")
        };

        if (options.DiagnosticsEmitPath is not null)
        {
            if (!TryWriteOutputFile(options.DiagnosticsEmitPath, diagnosticsOutput, out string? fullDiagnosticsPath, out string? diagnosticsWriteError))
            {
                Console.Error.WriteLine(diagnosticsWriteError);
                writeFailed = true;
            }
            else
            {
                completionMessages.Add($"Stored diagnostics at {fullDiagnosticsPath}.");
            }
        }
        else if (!string.IsNullOrEmpty(diagnosticsOutput))
        {
            WriteStatusSeparatorIfNeeded();
            Console.Out.Write(diagnosticsOutput);
        }

        if (!writeFailed)
        {
            WriteStatusSeparatorIfNeeded();

            for (int i = 0; i < completionMessages.Count; i++)
                WriteStatus(completionMessages[i]);

            WriteStatusSeparatorIfNeeded();
        }

        return hasErrors || writeFailed ? 1 : 0;
    }

    private static FileResult AnalyzeFile(string filePath, string displayPath, AnalysisOutput output, AnalysisProgress? progress)
    {
        try
        {
            progress?.ReportAnalyzing(displayPath);

            CompilerAnalysisResult analysis = MahoCompiler.AnalyzeFile(filePath, output);
            return new FileResult(filePath, displayPath, analysis, null, IsInternalError: false, analysis.HasErrors);
        }
        catch (Exception ex)
        {
            return new FileResult(filePath, displayPath, null, FormatAnalysisError(ex, filePath), IsInternalError: !IsUserFacingError(ex), HasErrors: true);
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

            if (result.Analysis is CompilerAnalysisResult analysis)
            {
                if (analysis.LexerJson is string lexerJson)
                    fileObject["lexer"] = JsonNode.Parse(lexerJson);

                if (analysis.ParserJson is string parserJson)
                    fileObject["parser"] = JsonNode.Parse(parserJson);

                fileObject["diagnostics"] = JsonNode.Parse(analysis.DiagnosticsJson);
            }

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

    private static string BuildDiagnosticsTextOutput(IReadOnlyList<FileResult> results, bool useColor)
    {
        StringBuilder sb = new();

        for (int i = 0; i < results.Count; i++)
        {
            FileResult result = results[i];

            if (result.Analysis is CompilerAnalysisResult analysis)
            {
                string diagnosticOutput = SerializedAnalysisRenderer.RenderDiagnosticsOutput(analysis, result.DisplayPath, useColor);

                if (!string.IsNullOrEmpty(diagnosticOutput))
                    sb.Append(diagnosticOutput);

                continue;
            }

            if (!string.IsNullOrEmpty(result.AnalysisError))
            {
                sb.Append(result.IsInternalError
                    ? SerializedAnalysisRenderer.RenderInternalFailure(result.DisplayPath, result.AnalysisError, useColor)
                    : SerializedAnalysisRenderer.RenderUserFacingFailure(result.DisplayPath, result.AnalysisError, useColor));
            }
        }

        return sb.ToString();
    }

    private static string BuildDiagnosticsJsonOutput(string inputPath, IReadOnlyList<FileResult> results)
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

            if (result.Analysis is CompilerAnalysisResult analysis)
                fileObject["diagnostics"] = JsonNode.Parse(analysis.DiagnosticsJson);
            else
                fileObject["diagnostics"] = new JsonArray();

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
            errorMessage = $"Failed to write output file '{outputPath}': {FormatPathOrIoError(ex, outputPath, "write the output")}";
            return false;
        }
    }

    private static bool TryResolveInputFiles(string sourcePath, out List<string> files, out string displayRoot, out string? errorMessage)
    {
        files = [];
        displayRoot = sourcePath;

        try
        {
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
        catch (Exception ex) when (IsUserFacingError(ex))
        {
            errorMessage = $"Failed to inspect input path '{sourcePath}': {FormatPathOrIoError(ex, sourcePath, "inspect the input path")}";
            return false;
        }
    }

    private static bool TryGetSourcePath(string? sourcePathArgument, out string sourcePath, out string? errorMessage)
    {
        try
        {
            sourcePath = sourcePathArgument is null
                ? GetDefaultSourcePath()
                : Path.GetFullPath(sourcePathArgument);

            errorMessage = null;
            return true;
        }
        catch (Exception ex) when (IsUserFacingError(ex))
        {
            sourcePath = string.Empty;
            errorMessage = $"Invalid source path '{sourcePathArgument}': {FormatPathOrIoError(ex, sourcePathArgument, "resolve the source path")}";
            return false;
        }
    }

    private static string FormatAnalysisError(Exception ex, string filePath)
    {
        if (!IsUserFacingError(ex))
            return ex.Message;

        return FormatPathOrIoError(ex, filePath, "analyze the file");
    }

    private static bool IsUserFacingError(Exception ex) =>
        ex is ArgumentException
            or UnauthorizedAccessException
            or PathTooLongException
            or DirectoryNotFoundException
            or FileNotFoundException
            or IOException
            or NotSupportedException;

    private static string FormatPathOrIoError(Exception ex, string? path, string action)
    {
        return ex switch
        {
            FileNotFoundException => $"source file not found: {path}.",
            DirectoryNotFoundException => $"directory not found: {path}.",
            UnauthorizedAccessException => $"access denied while trying to {action}: {path}.",
            PathTooLongException => $"path is too long: {path}.",
            NotSupportedException => $"path format is not supported: {path}.",
            ArgumentException => string.IsNullOrWhiteSpace(ex.Message)
                ? $"invalid path: {path}."
                : ex.Message,
            IOException => $"I/O error while trying to {action}: {ex.Message}",
            _ => ex.Message
        };
    }

    private static bool TryParseArguments(string[] args, out CliOptions options, out string? errorMessage)
    {
        AnalysisOutput output = AnalysisOutput.None;
        bool showHelp = false;
        bool showProgress = false;
        string? emitPath = null;
        string? diagnosticsEmitPath = null;
        DiagnosticOutputFormat diagnosticsFormat = DiagnosticOutputFormat.Text;
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

                case "--diagnostics-output":
                    if (!TryReadArgumentValue(args, ref i, arg, out diagnosticsEmitPath, out errorMessage))
                    {
                        options = default;
                        return false;
                    }

                    break;

                case "--diagnostics-format":
                    if (!TryReadArgumentValue(args, ref i, arg, out string? diagnosticsFormatValue, out errorMessage))
                    {
                        options = default;
                        return false;
                    }

                    if (!TryParseDiagnosticsFormat(diagnosticsFormatValue, out diagnosticsFormat, out errorMessage))
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

        options = new CliOptions(output, showHelp, showProgress, emitPath, diagnosticsEmitPath, diagnosticsFormat, sourcePath);
        errorMessage = null;
        return true;
    }

    private static bool TryParseDiagnosticsFormat(string? value, out DiagnosticOutputFormat format, out string? errorMessage)
    {
        switch (value)
        {
            case "text":
            case "txt":
                format = DiagnosticOutputFormat.Text;
                errorMessage = null;
                return true;

            case "json":
                format = DiagnosticOutputFormat.Json;
                errorMessage = null;
                return true;

            default:
                format = default;
                errorMessage = $"Unknown diagnostics format '{value}'. Use 'text' or 'json'.";
                return false;
        }
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
        writer.WriteLine("  -l, --lex                 Print the lexer token stream.");
        writer.WriteLine("  -p, --parse               Print the parser syntax tree.");
        writer.WriteLine("  -a, --all                 Print both debug views.");
        writer.WriteLine("      --progress            Show per-file analysis progress on stderr.");
        writer.WriteLine("  -o, --output              Write the requested debug views as JSON to the specified file.");
        writer.WriteLine("      --diagnostics-output  Write the final diagnostic report to the specified file.");
        writer.WriteLine("      --diagnostics-format  Use 'text' (default) or 'json' for diagnostics output.");
        writer.WriteLine("  -h, --help                Show this help text.");
        writer.WriteLine();
        writer.WriteLine("The source path may be a single '.mh' file or a directory.");
        writer.WriteLine("Directory inputs analyze every '.mh' file recursively.");
        writer.WriteLine("When no source path is provided, the current working directory is scanned recursively for '.mh' files.");
        writer.WriteLine("Debug views are printed to stdout when --output is not provided.");
        writer.WriteLine("Diagnostics are emitted after the full analysis pipeline finishes.");
        writer.WriteLine("When --diagnostics-output is omitted, diagnostics are printed to stdout in text format by default.");
    }

    private static void WriteStatus(string message)
    {
        lock (statusLock)
        {
            Console.Error.WriteLine(Colorize(message, BrightBlack));
            pendingStatusSeparator = true;
        }
    }

    private static void WriteStatusSeparatorIfNeeded()
    {
        lock (statusLock)
        {
            if (!pendingStatusSeparator)
                return;

            Console.Error.WriteLine();
            pendingStatusSeparator = false;
        }
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
