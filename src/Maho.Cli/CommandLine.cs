using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Maho.Cli;

/// <summary> Owns the terminal-facing compiler workflow and explicit debug/diagnostics routing. </summary>
internal static class CommandLine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private enum DiagnosticsFormat : byte
    {
        Text,
        Json
    }

    private readonly record struct CliOptions(
        AnalysisOutput DebugOutput,
        string? DebugDestination,
        bool DiagnosticsRequested,
        DiagnosticsFormat DiagnosticsFormat,
        string? DiagnosticsDestination,
        string? SourcePath);

    /// <summary> Executes the compiler driver and returns a process exit code. </summary>
    public static int Run(string[] args)
    {
        if (!TryParseArguments(args, out CliOptions options, out string? argumentError))
        {
            Console.Error.WriteLine(argumentError);
            Console.Error.WriteLine();
            PrintUsage(Console.Error);
            return 1;
        }

        if (options.SourcePath is "--help")
        {
            PrintUsage(Console.Out);
            return 0;
        }

        if (!TryGetSourcePath(options.SourcePath, out string sourcePath, out string? sourcePathError))
        {
            Console.Error.WriteLine(sourcePathError);
            return 1;
        }

        CompilerProjectAnalysisResult analysis;
        string? pipelineError = null;

        try
        {
            analysis = Compile(sourcePath, options.DebugOutput);
        }
        catch (CompilerPipelineNotImplementedException ex)
        {
            analysis = ex.Analysis;
            pipelineError = ex.Message;
        }
        catch (Exception ex) when (IsUserFacingError(ex))
        {
            Console.Error.WriteLine($"Failed to compile '{sourcePath}': {FormatPathOrIoError(ex, sourcePath, "compile the input")}");
            return 1;
        }

        bool writeFailed = false;

        if (options.DebugOutput is not AnalysisOutput.None)
        {
            string debugOutput = BuildDebugOutput(sourcePath, analysis);
            writeFailed |= !WriteOutput(options.DebugDestination, debugOutput, Console.Out, "debug output");
        }

        string diagnosticsOutput = options.DiagnosticsFormat is DiagnosticsFormat.Json
            ? BuildDiagnosticsJsonOutput(sourcePath, analysis, pipelineError)
            : BuildDiagnosticsTextOutput(sourcePath, analysis, pipelineError);

        if (options.DiagnosticsRequested || !string.IsNullOrEmpty(diagnosticsOutput))
            writeFailed |= !WriteOutput(options.DiagnosticsDestination, diagnosticsOutput, Console.Error, "diagnostics");

        return analysis.HasErrors || pipelineError is not null || writeFailed ? 1 : 0;
    }

    private static CompilerProjectAnalysisResult Compile(string sourcePath, AnalysisOutput debugOutput)
    {
        if (string.Equals(Path.GetExtension(sourcePath), ".mhpr", StringComparison.OrdinalIgnoreCase))
            return MahoCompiler.CompileProjectFile(sourcePath, debugOutput);

        if (!TryResolveInputFiles(sourcePath, out string[] files, out string? resolutionError))
            throw new ArgumentException(resolutionError, nameof(sourcePath));

        return MahoCompiler.CompileFiles(files, debugOutput, sourcePath);
    }

    private static string BuildDebugOutput(string inputPath, CompilerProjectAnalysisResult analysis)
    {
        JsonArray files = [];

        foreach (CompilerBatchFileResult file in analysis.Files)
        {
            JsonObject fileOutput = new()
            {
                ["filePath"] = file.SourcePath
            };

            if (file.Analysis is CompilerAnalysisResult fileAnalysis)
            {
                if (fileAnalysis.LexerJson is string lexerJson)
                    fileOutput["lexer"] = JsonNode.Parse(lexerJson);

                if (fileAnalysis.ParserJson is string parserJson)
                    fileOutput["parser"] = JsonNode.Parse(parserJson);
            }

            if (file.AnalysisError is string analysisError)
                fileOutput["analysisError"] = analysisError;

            files.Add(fileOutput);
        }

        return new JsonObject
        {
            ["inputPath"] = inputPath,
            ["files"] = files
        }.ToJsonString(JsonOptions);
    }

    private static string BuildDiagnosticsTextOutput(string inputPath, CompilerProjectAnalysisResult analysis, string? pipelineError)
    {
        StringBuilder output = new();
        bool multipleFiles = analysis.Files.Length > 1;
        string displayRoot = string.Equals(Path.GetExtension(inputPath), ".mhpr", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(inputPath) ?? inputPath
            : Directory.Exists(inputPath) ? inputPath : Path.GetDirectoryName(inputPath) ?? inputPath;

        foreach (CompilerBatchFileResult file in analysis.Files)
        {
            string displayPath = multipleFiles ? Path.GetRelativePath(displayRoot, file.SourcePath) : file.SourcePath;

            if (file.Analysis is CompilerAnalysisResult fileAnalysis)
            {
                foreach (DiagnosticInfo diagnostic in fileAnalysis.Diagnostics)
                {
                    output.Append(displayPath);
                    output.Append('(');
                    output.Append(diagnostic.Span.StartLocation.Line);
                    output.Append(',');
                    output.Append(diagnostic.Span.StartLocation.Column);
                    output.Append("): ");
                    output.Append(diagnostic.Severity.ToString().ToLowerInvariant());
                    output.Append(' ');
                    output.Append(diagnostic.Code);
                    output.Append(": ");
                    output.AppendLine(diagnostic.Message);
                }
            }
            else if (file.AnalysisError is string analysisError)
            {
                output.Append(displayPath);
                output.Append(": error MH9001: ");
                output.AppendLine(analysisError);
            }
        }

        if (pipelineError is not null)
            output.AppendLine($"error MH9000: {pipelineError}");

        return output.ToString();
    }

    private static string BuildDiagnosticsJsonOutput(string inputPath, CompilerProjectAnalysisResult analysis, string? pipelineError)
    {
        JsonArray files = [];

        foreach (CompilerBatchFileResult file in analysis.Files)
        {
            JsonObject fileOutput = new()
            {
                ["filePath"] = file.SourcePath,
                ["diagnostics"] = file.Analysis is CompilerAnalysisResult fileAnalysis
                    ? JsonSerializer.SerializeToNode(fileAnalysis.Diagnostics, JsonOptions)
                    : new JsonArray()
            };

            if (file.AnalysisError is string analysisError)
                fileOutput["analysisError"] = analysisError;

            files.Add(fileOutput);
        }

        JsonObject output = new()
        {
            ["inputPath"] = inputPath,
            ["files"] = files
        };

        if (pipelineError is not null)
        {
            output["pipelineDiagnostic"] = new JsonObject
            {
                ["code"] = "MH9000",
                ["message"] = pipelineError
            };
        }

        return output.ToJsonString(JsonOptions);
    }

    private static bool WriteOutput(string? destination, string output, TextWriter standardStream, string outputName)
    {
        if (destination is null)
        {
            standardStream.Write(output);
            return true;
        }

        try
        {
            string fullPath = Path.GetFullPath(destination);
            string? directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(fullPath, output);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to write {outputName} to '{destination}': {FormatPathOrIoError(ex, destination, "write the output")}");
            return false;
        }
    }

    private static bool TryResolveInputFiles(string sourcePath, out string[] files, out string? errorMessage)
    {
        files = [];

        if (File.Exists(sourcePath))
        {
            files = [sourcePath];
            errorMessage = null;
            return true;
        }

        if (!Directory.Exists(sourcePath))
        {
            errorMessage = $"Input path not found: {sourcePath}";
            return false;
        }

        files = Directory.GetFiles(sourcePath, "*.mh", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        if (files.Length == 0)
        {
            errorMessage = $"No '.mh' files were found in directory: {sourcePath}";
            return false;
        }

        errorMessage = null;
        return true;
    }

    private static bool TryGetSourcePath(string? sourcePathArgument, out string sourcePath, out string? errorMessage)
    {
        try
        {
            sourcePath = sourcePathArgument is null
                ? Path.GetFullPath(Directory.GetCurrentDirectory())
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

    private static bool TryParseArguments(string[] args, out CliOptions options, out string? errorMessage)
    {
        AnalysisOutput debugOutput = AnalysisOutput.None;
        string? debugDestination = null;
        bool diagnosticsRequested = false;
        DiagnosticsFormat diagnosticsFormat = DiagnosticsFormat.Text;
        string? diagnosticsDestination = null;
        string? sourcePath = null;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            switch (argument)
            {
                case "-h":
                case "--help":
                    if (args.Length != 1)
                    {
                        options = default;
                        errorMessage = "The help option cannot be combined with other arguments.";
                        return false;
                    }

                    options = new CliOptions(AnalysisOutput.None, null, false, DiagnosticsFormat.Text, null, "--help");
                    errorMessage = null;
                    return true;

                case "--debug":
                    if (debugOutput is not AnalysisOutput.None)
                    {
                        options = default;
                        errorMessage = "The --debug option can only be specified once.";
                        return false;
                    }

                    if (!TryReadDebugRequest(args, ref index, out debugOutput, out debugDestination, out errorMessage))
                    {
                        options = default;
                        return false;
                    }

                    break;

                case "--diagnostics":
                    if (diagnosticsRequested)
                    {
                        options = default;
                        errorMessage = "The --diagnostics option can only be specified once.";
                        return false;
                    }

                    if (!TryReadDiagnosticsRequest(args, ref index, out diagnosticsFormat, out diagnosticsDestination, out errorMessage))
                    {
                        options = default;
                        return false;
                    }

                    diagnosticsRequested = true;
                    break;

                case "--lex":
                case "--parse":
                    options = default;
                    errorMessage = $"The '{argument}' selector must follow --debug.";
                    return false;

                default:
                    if (argument.Length > 0 && argument[0] == '-')
                    {
                        options = default;
                        errorMessage = $"Unknown option '{argument}'.";
                        return false;
                    }

                    if (sourcePath is not null)
                    {
                        options = default;
                        errorMessage = "Only one source file, project file, or directory can be provided.";
                        return false;
                    }

                    sourcePath = argument;
                    break;
            }
        }

        options = new CliOptions(debugOutput, debugDestination, diagnosticsRequested, diagnosticsFormat, diagnosticsDestination, sourcePath);
        errorMessage = null;
        return true;
    }

    private static bool TryReadDebugRequest(string[] args, ref int index, out AnalysisOutput output, out string? destination, out string? errorMessage)
    {
        output = AnalysisOutput.None;
        destination = null;

        while (++index < args.Length && args[index] != "--output")
        {
            AnalysisOutput selector = args[index] switch
            {
                "--lex" => AnalysisOutput.Lexer,
                "--parse" => AnalysisOutput.Parser,
                _ => AnalysisOutput.None
            };

            if (selector is AnalysisOutput.None)
            {
                errorMessage = $"Unknown debug selector '{args[index]}'.";
                return false;
            }

            output |= selector;
        }

        if (output is AnalysisOutput.None)
        {
            errorMessage = "The --debug option requires one or more of --lex and --parse.";
            return false;
        }

        if (index >= args.Length)
        {
            errorMessage = "The --debug option requires --output followed by a destination path or '-'.";
            return false;
        }

        if (++index >= args.Length || (args[index] != "-" && args[index].StartsWith("-", StringComparison.Ordinal)))
        {
            errorMessage = "The --debug option requires a destination path or '-' after --output.";
            return false;
        }

        destination = ParseDestination(args[index]);
        errorMessage = null;
        return true;
    }

    private static bool TryReadDiagnosticsRequest(string[] args, ref int index, out DiagnosticsFormat format, out string? destination, out string? errorMessage)
    {
        format = DiagnosticsFormat.Text;
        destination = null;

        while (++index < args.Length && args[index] != "--output")
        {
            switch (args[index])
            {
                case "--text":
                    format = DiagnosticsFormat.Text;
                    break;
                case "--json":
                    format = DiagnosticsFormat.Json;
                    break;
                default:
                    errorMessage = $"Unknown diagnostics selector '{args[index]}'.";
                    return false;
            }
        }

        if (index >= args.Length)
        {
            errorMessage = "The --diagnostics option requires --output followed by a destination path or '-'.";
            return false;
        }

        if (++index >= args.Length || (args[index] != "-" && args[index].StartsWith("-", StringComparison.Ordinal)))
        {
            errorMessage = "The --diagnostics option requires a destination path or '-' after --output.";
            return false;
        }

        destination = ParseDestination(args[index]);
        errorMessage = null;
        return true;
    }

    private static string? ParseDestination(string value) => value is "-" ? null : value;

    private static bool IsUserFacingError(Exception exception) =>
        exception is ArgumentException
            or UnauthorizedAccessException
            or PathTooLongException
            or DirectoryNotFoundException
            or FileNotFoundException
            or IOException
            or NotSupportedException;

    private static string FormatPathOrIoError(Exception exception, string? path, string action)
    {
        return exception switch
        {
            FileNotFoundException => $"source file not found: {path}.",
            DirectoryNotFoundException => $"directory not found: {path}.",
            UnauthorizedAccessException => $"access denied while trying to {action}: {path}.",
            PathTooLongException => $"path is too long: {path}.",
            NotSupportedException => $"path format is not supported: {path}.",
            ArgumentException => exception.Message,
            IOException => $"I/O error while trying to {action}: {exception.Message}",
            _ => exception.Message
        };
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: maho [options] [source-path]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --debug (--lex|--parse)+ --output <file|->");
        writer.WriteLine("                                 Write selected debug JSON to a file or stdout.");
        writer.WriteLine("  --diagnostics [--text|--json] --output <file|->");
        writer.WriteLine("                                 Write diagnostics to a file or stderr.");
        writer.WriteLine("  -h, --help                     Show this help text.");
        writer.WriteLine();
        writer.WriteLine("The source path may be a '.mh' file, a '.mhpr' project file, or a directory.");
        writer.WriteLine("Directories are searched recursively for '.mh' files. '-' selects stdout for debug and stderr for diagnostics.");
    }
}
