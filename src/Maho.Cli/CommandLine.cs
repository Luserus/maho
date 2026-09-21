using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Maho.Analysis;
using Maho.Build;
using Maho.Cli.Diagnostics;

namespace Maho.Cli;

/// <summary> Owns the terminal-facing compiler workflow and explicit debug/diagnostics routing. </summary>
public static class CommandLine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private enum DiagnosticsFormat : byte
    {
        Pretty,
        Text,
        Json
    }

    private readonly record struct CliOptions(
        AnalysisOutput DebugOutput,
        string? DebugDestination,
        bool DiagnosticsRequested,
        DiagnosticsFormat DiagnosticsFormat,
        string? DiagnosticsDestination,
        DiagnosticColorMode ColorMode,
        DiagnosticPathStyle PathStyle,
        bool WarningsAsErrors,
        bool ShowHelp,
        bool ShowVersion,
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

        if (options.ShowHelp)
        {
            PrintUsage(Console.Out);
            return 0;
        }

        if (options.ShowVersion)
        {
            PrintVersion(Console.Out);
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

        string diagnosticsOutput = options.DiagnosticsFormat switch
        {
            DiagnosticsFormat.Json => BuildDiagnosticsJsonOutput(sourcePath, analysis, pipelineError),
            DiagnosticsFormat.Text => BuildDiagnosticsTextOutput(sourcePath, analysis, pipelineError),
            _ => BuildDiagnosticsPrettyOutput(sourcePath, analysis, pipelineError, options.ColorMode, options.PathStyle)
        };

        if (options.DiagnosticsRequested || !string.IsNullOrEmpty(diagnosticsOutput))
            writeFailed |= !WriteOutput(options.DiagnosticsDestination, diagnosticsOutput, Console.Error, "diagnostics");

        bool hasErrors = analysis.HasErrors ||
            (options.WarningsAsErrors && analysis.Files.Any(f => f.Analysis?.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning) == true));

        return hasErrors || pipelineError is not null || writeFailed ? 1 : 0;
    }

    private static CompilerProjectAnalysisResult Compile(string sourcePath, AnalysisOutput debugOutput)
    {
        if (string.Equals(Path.GetExtension(sourcePath), ".mhpr", StringComparison.OrdinalIgnoreCase))
            return MahoBuildSystem.CompileProject(sourcePath, debugOutput);

        if (File.Exists(sourcePath))
        {
            string fullPath = Path.GetFullPath(sourcePath);
            var options = CompilationOptions.Default with { ImplicitTopLevel = true, EntryFile = fullPath };
            return MahoCompiler.CompileFiles([fullPath], debugOutput, fullPath, options);
        }

        if (Directory.Exists(sourcePath))
        {
            return MahoBuildSystem.CompileDirectory(sourcePath, debugOutput);
        }

        throw new ArgumentException($"Input path not found: {sourcePath}", nameof(sourcePath));
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

            if (file.DebugOutput is { } fileAnalysis)
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

            if (file.Output is { } fileAnalysis)
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

    private static string BuildDiagnosticsPrettyOutput(
        string inputPath,
        CompilerProjectAnalysisResult analysis,
        string? pipelineError,
        DiagnosticColorMode colorMode,
        DiagnosticPathStyle pathStyle)
    {
        var renderer = new TerminalDiagnosticRenderer(colorMode, pathStyle, Directory.GetCurrentDirectory());
        var sb = new StringBuilder();

        foreach (CompilerBatchFileResult file in analysis.Files)
        {
            if (file.Output is { } fileAnalysis)
            {
                foreach (DiagnosticInfo diagnostic in fileAnalysis.Diagnostics)
                {
                    sb.Append(renderer.Render(diagnostic));
                }
            }
            else if (file.AnalysisError is string analysisError)
            {
                sb.AppendLine($"error[MH9001]: {analysisError}");
            }
        }

        if (pipelineError is not null)
            sb.AppendLine($"error[MH9000]: {pipelineError}");

        return sb.ToString();
    }

    private static string BuildDiagnosticsJsonOutput(string inputPath, CompilerProjectAnalysisResult analysis, string? pipelineError)
    {
        JsonArray files = [];

        foreach (CompilerBatchFileResult file in analysis.Files)
        {
            JsonObject fileOutput = new()
            {
                ["filePath"] = file.SourcePath,
                ["diagnostics"] = file.Output is { } fileAnalysis
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
        try
        {
            files = MahoBuildSystem.ResolveSourceFiles(sourcePath);
            errorMessage = null;
            return true;
        }
        catch (Exception ex) when (IsUserFacingError(ex))
        {
            errorMessage = ex.Message;
            return false;
        }
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
        DiagnosticsFormat diagnosticsFormat = DiagnosticsFormat.Pretty;
        string? diagnosticsDestination = null;
        DiagnosticColorMode colorMode = DiagnosticColorMode.Auto;
        DiagnosticPathStyle pathStyle = DiagnosticPathStyle.Relative;
        bool warningsAsErrors = false;
        bool showHelp = false;
        bool showVersion = false;
        string? sourcePath = null;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            switch (argument)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;

                case "-v":
                case "--version":
                    showVersion = true;
                    break;

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

                case "--color":
                    if (index + 1 < args.Length && !args[index + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        index++;
                        colorMode = args[index].ToLowerInvariant() switch
                        {
                            "auto" => DiagnosticColorMode.Auto,
                            "always" => DiagnosticColorMode.Always,
                            "never" => DiagnosticColorMode.Never,
                            _ => (DiagnosticColorMode?)null
                        } ?? throw new ArgumentException($"Invalid color mode '{args[index]}'. Expected auto, always, or never.");
                    }
                    else
                    {
                        colorMode = DiagnosticColorMode.Always;
                    }
                    break;

                case "--no-color":
                    colorMode = DiagnosticColorMode.Never;
                    break;

                case "--diagnostic-paths":
                    if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        options = default;
                        errorMessage = "The --diagnostic-paths option requires 'relative' or 'full'.";
                        return false;
                    }
                    index++;
                    pathStyle = args[index].ToLowerInvariant() switch
                    {
                        "relative" => DiagnosticPathStyle.Relative,
                        "full" => DiagnosticPathStyle.Full,
                        _ => (DiagnosticPathStyle?)null
                    } ?? throw new ArgumentException($"Invalid path style '{args[index]}'. Expected relative or full.");
                    break;

                case "-Werror":
                case "--warnings-as-errors":
                    warningsAsErrors = true;
                    break;

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

        options = new CliOptions(debugOutput, debugDestination, diagnosticsRequested, diagnosticsFormat, diagnosticsDestination, colorMode, pathStyle, warningsAsErrors, showHelp, showVersion, sourcePath);
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
                "lex" => AnalysisOutput.Lexer,
                "parse" => AnalysisOutput.Parser,
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
            errorMessage = "The --debug option requires one or more of lex and parse.";
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
        format = DiagnosticsFormat.Pretty;
        destination = null;

        while (++index < args.Length && args[index] != "--output")
        {
            switch (args[index].ToLowerInvariant())
            {
                case "pretty":
                    format = DiagnosticsFormat.Pretty;
                    break;
                case "text":
                    format = DiagnosticsFormat.Text;
                    break;
                case "json":
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

    private static void PrintVersion(TextWriter writer) => writer.WriteLine($"Maho: v{MahoCompiler.VersionString}");

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: maho [options] [source-path]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --debug (lex|parse)+ --output <file|->                      Write selected debug JSON to a file or stdout.");
        writer.WriteLine("  --diagnostics [pretty|text|json] --output <file|->          Write diagnostics to a file or stderr (default: pretty).");
        writer.WriteLine("  --color [auto|always|never]                                 Control ANSI colored diagnostics.");
        writer.WriteLine("  --no-color                                                  Disable colored diagnostics.");
        writer.WriteLine("  --diagnostic-paths (relative|full)                          Choose relative or full file paths in diagnostics.");
        writer.WriteLine("  -Werror, --warnings-as-errors                               Treat compiler warnings as errors.");
        writer.WriteLine("  -v, --version                                               Show compiler version.");
        writer.WriteLine("  -h, --help                                                  Show this help text.");
        writer.WriteLine();
        writer.WriteLine("The source path may be a '.mh' file, a '.mhpr' project file, or a directory.");
        writer.WriteLine("Directories are searched recursively for '.mh' files. '-' selects stdout for debug and stderr for diagnostics.");
    }
}