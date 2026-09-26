using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Maho;

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
        string? OutputDestination,
        IReadOnlyList<string> SourceInputs,
        string? Pattern,
        string? EntryFile,
        bool? ImplicitTopLevel = null,
        bool EmitIl = false,
        bool CheckOnly = false,
        bool NoRecurse = false,
        IReadOnlyDictionary<string, string>? GlobalAliases = null);

    /// <summary> Executes the compiler driver and returns a process exit code. </summary>
    public static int Run(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // Ignore if console output encoding cannot be changed
        }

        if (args.Length == 0)
        {
            PrintUsage(Console.Out);
            return 0;
        }

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

        string primaryInput = options.SourceInputs.Count > 0 ? options.SourceInputs[0] : Directory.GetCurrentDirectory();
        CompilerProjectAnalysisResult analysis;
        string? pipelineError = null;

        try
        {
            analysis = Compile(options);
        }
        catch (CompilerPipelineNotImplementedException ex)
        {
            analysis = ex.Analysis;
            pipelineError = ex.Message;
        }
        catch (Exception ex) when (IsUserFacingError(ex))
        {
            Console.Error.WriteLine($"Failed to compile '{primaryInput}': {FormatPathOrIoError(ex, primaryInput, "compile the input")}");
            return 1;
        }

        bool writeFailed = false;

        if (options.DebugOutput is not AnalysisOutput.None)
        {
            string debugOutput = BuildDebugOutput(primaryInput, analysis);
            writeFailed |= !WriteOutput(options.DebugDestination, debugOutput, Console.Out, "debug output");
        }

        string diagnosticsOutput = options.DiagnosticsFormat switch
        {
            DiagnosticsFormat.Json => BuildDiagnosticsJsonOutput(primaryInput, analysis, pipelineError),
            DiagnosticsFormat.Text => BuildDiagnosticsTextOutput(primaryInput, analysis, pipelineError, options.PathStyle),
            _ => BuildDiagnosticsPrettyOutput(primaryInput, analysis, pipelineError, options.ColorMode, options.PathStyle)
        };

        if (options.DiagnosticsRequested || !string.IsNullOrEmpty(diagnosticsOutput))
            writeFailed |= !WriteOutput(options.DiagnosticsDestination, diagnosticsOutput, Console.Error, "diagnostics");

        bool hasErrors = analysis.HasErrors ||
            (options.WarningsAsErrors && analysis.Files.Any(f => f.Analysis?.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning) == true));

        return hasErrors || pipelineError is not null || writeFailed ? 1 : 0;
    }

    private static (List<string> Files, string RootPath) ResolveFiles(IReadOnlyList<string> inputs, string pattern, bool noRecurse)
    {
        var inputList = inputs.Count == 0 ? ["."] : inputs;
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? rootDir = null;
        var searchOption = noRecurse ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;

        foreach (string input in inputList)
        {
            if (Directory.Exists(input))
            {
                string fullDir = Path.GetFullPath(input);
                rootDir ??= fullDir;
                string[] matching = Directory.GetFiles(fullDir, pattern, searchOption);
                if (matching.Length == 0)
                    throw new FileNotFoundException($"No source files found in directory: {input}");

                foreach (string m in matching)
                    files.Add(Path.GetFullPath(m));
            }
            else if (File.Exists(input))
            {
                string fullFile = Path.GetFullPath(input);
                rootDir ??= Path.GetDirectoryName(fullFile);
                files.Add(fullFile);
            }
            else
            {
                throw new FileNotFoundException($"Input path not found: {input}", input);
            }
        }

        if (files.Count == 0)
            throw new FileNotFoundException("No source files found to compile.");

        var sortedFiles = files.ToList();
        sortedFiles.Sort(StringComparer.Ordinal);
        return (sortedFiles, rootDir ?? Directory.GetCurrentDirectory());
    }

    private static CompilerProjectAnalysisResult Compile(CliOptions cliOptions)
    {
        var (files, rootPath) = ResolveFiles(cliOptions.SourceInputs, cliOptions.Pattern ?? "*.mh", cliOptions.NoRecurse);

        string? resolvedEntry = null;
        if (cliOptions.EntryFile is not null)
        {
            resolvedEntry = Path.IsPathRooted(cliOptions.EntryFile)
                ? Path.GetFullPath(cliOptions.EntryFile)
                : Path.GetFullPath(Path.Combine(rootPath, cliOptions.EntryFile));

            if (!File.Exists(resolvedEntry))
                throw new FileNotFoundException($"Configured EntryFile not found: {resolvedEntry}", resolvedEntry);
        }

        var compOptions = new CompilationOptions
        {
            ColorMode = cliOptions.ColorMode,
            PathStyle = cliOptions.PathStyle,
            WarningsAsErrors = cliOptions.WarningsAsErrors,
            OutputFormat = cliOptions.DiagnosticsFormat switch
            {
                DiagnosticsFormat.Json => DiagnosticFormat.Json,
                DiagnosticsFormat.Text => DiagnosticFormat.Short,
                _ => DiagnosticFormat.Pretty
            },
            ImplicitTopLevel = cliOptions.ImplicitTopLevel ?? true,
            EntryFile = resolvedEntry,
            RootDirectory = rootPath,
            GlobalAliases = cliOptions.GlobalAliases ?? new Dictionary<string, string>()
        };

        if (cliOptions.CheckOnly)
        {
            return MahoCompiler.AnalyzeFiles(files, cliOptions.DebugOutput, rootPath, compOptions);
        }

        return MahoCompiler.CompileFiles(files, cliOptions.DebugOutput, rootPath, compOptions);
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

            files.Add((JsonNode)fileOutput);
        }

        return new JsonObject
        {
            ["inputPath"] = inputPath,
            ["files"] = files
        }.ToJsonString(JsonOptions);
    }

    private static string GetDisplayRoot(string? inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return Directory.GetCurrentDirectory();

        try
        {
            if (string.Equals(Path.GetExtension(inputPath), ".mhpr", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Directory.GetCurrentDirectory();

            if (Directory.Exists(inputPath))
                return Path.GetFullPath(inputPath);

            return Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Directory.GetCurrentDirectory();
        }
        catch
        {
            return Directory.GetCurrentDirectory();
        }
    }

    private static string BuildDiagnosticsTextOutput(
        string inputPath,
        CompilerProjectAnalysisResult analysis,
        string? pipelineError,
        DiagnosticPathStyle pathStyle = DiagnosticPathStyle.Relative)
    {
        StringBuilder output = new();
        string displayRoot = pathStyle switch
        {
            DiagnosticPathStyle.ProjectRelative => GetDisplayRoot(inputPath),
            _ => Directory.GetCurrentDirectory()
        };

        int errorCount = 0;
        int warningCount = 0;

        foreach (CompilerBatchFileResult file in analysis.Files)
        {
            string displayPath = pathStyle switch
            {
                DiagnosticPathStyle.Full => Path.GetFullPath(file.SourcePath),
                _ => Path.GetRelativePath(displayRoot, file.SourcePath)
            };

            if (file.Output is { } fileAnalysis)
            {
                foreach (DiagnosticInfo diagnostic in fileAnalysis.Diagnostics)
                {
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                        errorCount++;
                    else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                        warningCount++;

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
                errorCount++;
                output.Append(displayPath);
                output.Append(": error MH9001: ");
                output.AppendLine(analysisError);
            }
        }

        if (pipelineError is not null)
        {
            errorCount++;
            output.AppendLine($"error MH9000: {pipelineError}");
        }

        string timeStr = TerminalDiagnosticRenderer.FormatTime(analysis.Elapsed);

        if (errorCount == 0 && warningCount == 0)
            output.AppendLine($"Build succeeded in {timeStr}");
        else if (errorCount > 0 && warningCount > 0)
            output.AppendLine($"Build failed with {errorCount} error(s) and {warningCount} warning(s)");
        else if (errorCount > 0)
            output.AppendLine($"Build failed with {errorCount} error(s)");
        else
            output.AppendLine($"Build succeeded with {warningCount} warning(s) in {timeStr}");

        return output.ToString();
    }

    private static string BuildDiagnosticsPrettyOutput(
        string inputPath,
        CompilerProjectAnalysisResult analysis,
        string? pipelineError,
        DiagnosticColorMode colorMode,
        DiagnosticPathStyle pathStyle)
    {
        string projectRoot = GetDisplayRoot(inputPath);
        var renderer = new TerminalDiagnosticRenderer(
            colorMode,
            pathStyle,
            rootDirectory: Directory.GetCurrentDirectory(),
            projectDirectory: projectRoot);
        var sb = new StringBuilder();

        int errorCount = 0;
        int warningCount = 0;

        foreach (CompilerBatchFileResult file in analysis.Files)
        {
            if (file.Output is { } fileAnalysis)
            {
                foreach (DiagnosticInfo diagnostic in fileAnalysis.Diagnostics)
                {
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                        errorCount++;
                    else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                        warningCount++;

                    sb.Append(renderer.Render(diagnostic));
                }
            }
            else if (file.AnalysisError is string analysisError)
            {
                errorCount++;
                sb.AppendLine($"error[MH9001]: {analysisError}");
            }
        }

        if (pipelineError is not null)
        {
            errorCount++;
            sb.AppendLine($"error[MH9000]: {pipelineError}");
        }

        sb.Append(renderer.RenderStatus(errorCount, warningCount, analysis.Elapsed));

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
                    ? JsonSerializer.SerializeToNode(fileAnalysis.Diagnostics, MahoJsonContext.Default.IReadOnlyListDiagnosticInfo)
                    : new JsonArray()
            };

            if (file.AnalysisError is string analysisError)
                fileOutput["analysisError"] = analysisError;

            files.Add((JsonNode)fileOutput);
        }

        JsonObject output = new()
        {
            ["inputPath"] = inputPath,
            ["files"] = files
        };

        if (pipelineError is not null)
            output["pipelineDiagnostic"] = new JsonObject
            {
                ["code"] = "MH0003",
                ["message"] = pipelineError
            };

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



    private static bool IsOutputOption(string arg) => arg is "-o" or "--output";

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
        string? generalOutput = null;
        var sourceInputs = new List<string>();
        string? pattern = null;
        string? entryFile = null;
        bool? implicitTopLevel = null;
        bool emitIl = false;
        bool checkOnly = false;
        bool noRecurse = false;
        var globalAliases = new Dictionary<string, string>(StringComparer.Ordinal);

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

                case "-o":
                case "--output":
                    if (generalOutput is not null)
                    {
                        options = default;
                        errorMessage = "The -o/--output option can only be specified once.";
                        return false;
                    }

                    if (index + 1 >= args.Length || (args[index + 1] != "-" && args[index + 1].StartsWith('-')))
                    {
                        options = default;
                        errorMessage = "The -o/--output option requires a destination path or '-'.";
                        return false;
                    }

                    index++;
                    generalOutput = ParseDestination(args[index]);
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

                case "--diagnostic-paths":
                    if (index + 1 >= args.Length || (args[index + 1] != "-" && args[index + 1].StartsWith('-')))
                    {
                        options = default;
                        errorMessage = "The --diagnostic-paths option requires 'relative', 'project', or 'full'.";
                        return false;
                    }
                    index++;
                    var parsedStyle = args[index].ToLowerInvariant() switch
                    {
                        "relative" or "cwd" => DiagnosticPathStyle.Relative,
                        "project" or "project-relative" => DiagnosticPathStyle.ProjectRelative,
                        "full" or "absolute" => DiagnosticPathStyle.Full,
                        _ => (DiagnosticPathStyle?)null
                    };
                    if (parsedStyle is null)
                    {
                        options = default;
                        errorMessage = $"Invalid path style '{args[index]}'. Expected relative, project, or full.";
                        return false;
                    }
                    pathStyle = parsedStyle.Value;
                    break;

                case "-Werror":
                case "--warnings-as-errors":
                    warningsAsErrors = true;
                    break;

                case "--pattern":
                    if (index + 1 >= args.Length || (args[index + 1] != "-" && args[index + 1].StartsWith('-')))
                    {
                        options = default;
                        errorMessage = "The --pattern option requires a search pattern (e.g. '*.mh').";
                        return false;
                    }
                    index++;
                    pattern = args[index];
                    break;

                case "--entry":
                    if (index + 1 >= args.Length || (args[index + 1] != "-" && args[index + 1].StartsWith('-')))
                    {
                        options = default;
                        errorMessage = "The --entry option requires a source file path.";
                        return false;
                    }
                    index++;
                    entryFile = args[index];
                    break;

                case "--emit-il":
                    emitIl = true;
                    break;

                case "--check":
                    checkOnly = true;
                    break;

                case "--no-recurse":
                    noRecurse = true;
                    break;

                case "-np":
                case "--no-project":
                case "--allow-no-project":
                    // Accepted for backwards compatibility
                    break;

                case string arg when arg.StartsWith("--implicit-toplevel=", StringComparison.OrdinalIgnoreCase):
                    string boolStr = arg[(arg.IndexOf('=') + 1)..];
                    if (bool.TryParse(boolStr, out bool parsedBool))
                    {
                        implicitTopLevel = parsedBool;
                    }
                    else
                    {
                        options = default;
                        errorMessage = $"Invalid value '{boolStr}' for --implicit-toplevel. Expected 'true' or 'false'.";
                        return false;
                    }
                    break;

                case "--implicit-toplevel":
                    implicitTopLevel = true;
                    break;

                case "--alias":
                case "--global-alias":
                    if (index + 1 >= args.Length || (args[index + 1] != "-" && args[index + 1].StartsWith('-')))
                    {
                        options = default;
                        errorMessage = $"The {argument} option requires an alias declaration in the format 'alias=target' (e.g. 'int32=Std.Int32').";
                        return false;
                    }
                    index++;
                    if (!TryParseAlias(args[index], globalAliases, out errorMessage))
                    {
                        options = default;
                        return false;
                    }
                    break;

                case string arg when arg.StartsWith("--alias=", StringComparison.OrdinalIgnoreCase) ||
                                     arg.StartsWith("--global-alias=", StringComparison.OrdinalIgnoreCase):
                    string aliasVal = arg[(arg.IndexOf('=') + 1)..];
                    if (!TryParseAlias(aliasVal, globalAliases, out errorMessage))
                    {
                        options = default;
                        return false;
                    }
                    break;

                default:
                    if (argument.Length > 0 && argument[0] == '-')
                    {
                        options = default;
                        errorMessage = $"Unknown option '{argument}'.";
                        return false;
                    }

                    sourceInputs.Add(argument);
                    break;
            }
        }

        debugDestination ??= generalOutput;
        diagnosticsDestination ??= generalOutput;

        options = new CliOptions(
            debugOutput,
            debugDestination,
            diagnosticsRequested,
            diagnosticsFormat,
            diagnosticsDestination,
            colorMode,
            pathStyle,
            warningsAsErrors,
            showHelp,
            showVersion,
            generalOutput,
            sourceInputs,
            pattern,
            entryFile,
            implicitTopLevel,
            emitIl,
            checkOnly,
            noRecurse,
            globalAliases.Count > 0 ? globalAliases : null);

        errorMessage = null;
        return true;
    }

    private static bool TryParseAlias(string input, Dictionary<string, string> target, out string? errorMessage)
    {
        errorMessage = null;
        string[] entries = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
        {
            errorMessage = "Expected an alias in the format 'alias=target' (e.g. 'int32=Std.Int32').";
            return false;
        }

        foreach (string entry in entries)
        {
            int sepIdx = entry.IndexOfAny(['=', ':']);
            if (sepIdx <= 0 || sepIdx == entry.Length - 1)
            {
                errorMessage = $"Invalid alias format '{entry}'. Expected 'alias=target' (e.g. 'int32=Std.Int32').";
                return false;
            }

            string aliasName = entry[..sepIdx].Trim();
            string targetType = entry[(sepIdx + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(aliasName) || string.IsNullOrWhiteSpace(targetType))
            {
                errorMessage = $"Invalid alias format '{entry}'. Alias name and target type cannot be empty.";
                return false;
            }

            target[aliasName] = targetType;
        }

        return true;
    }

    private static bool TryReadDebugRequest(string[] args, ref int index, out AnalysisOutput output, out string? destination, out string? errorMessage)
    {
        output = AnalysisOutput.None;
        destination = null;

        while (++index < args.Length && !IsOutputOption(args[index]))
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
            errorMessage = "The --debug option requires -o or --output followed by a destination path or '-'.";
            return false;
        }

        if (++index >= args.Length || (args[index] != "-" && args[index].StartsWith("-", StringComparison.Ordinal)))
        {
            errorMessage = "The --debug option requires a destination path or '-' after -o or --output.";
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

        while (++index < args.Length && !IsOutputOption(args[index]))
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
            errorMessage = "The --diagnostics option requires -o or --output followed by a destination path or '-'.";
            return false;
        }

        if (++index >= args.Length || (args[index] != "-" && args[index].StartsWith("-", StringComparison.Ordinal)))
        {
            errorMessage = "The --diagnostics option requires a destination path or '-' after -o or --output.";
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
            or NotSupportedException
            or InvalidOperationException;

    private static string FormatPathOrIoError(Exception exception, string? path, string action)
    {
        return exception switch
        {
            FileNotFoundException fnf when !string.IsNullOrWhiteSpace(fnf.Message) && !fnf.Message.StartsWith("Could not find file", StringComparison.OrdinalIgnoreCase) => fnf.Message,
            FileNotFoundException => $"source file not found: {path}.",
            DirectoryNotFoundException dnf when !string.IsNullOrWhiteSpace(dnf.Message) && !dnf.Message.StartsWith("Could not find a part of the path", StringComparison.OrdinalIgnoreCase) => dnf.Message,
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
        PrintVersion(writer);
        writer.WriteLine("A compiler frontend for the Maho programming language.");
        writer.WriteLine();
        writer.WriteLine("Usage: mahoc [options] [source-paths...]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --emit-il                                                   Compile and emit Maho Intermediate Language (IL).");
        writer.WriteLine("  --check                                                     Verify syntax and semantics without code generation.");
        writer.WriteLine("  --pattern <pattern>                                         File search pattern for directory inputs (default: '*.mh').");
        writer.WriteLine("  --no-recurse                                                Do not search subdirectories recursively when directory is passed.");
        writer.WriteLine("  --entry <file>                                              Specify the explicit entry point source file.");
        writer.WriteLine("  --alias <name=target>                                       Define a global type alias (e.g. 'int32=Std.Int32').");
        writer.WriteLine("  --debug (lex|parse)+ (-o|--output) <file|->                 Write selected debug JSON to a file or stdout.");
        writer.WriteLine("  --diagnostics [pretty|text|json] (-o|--output) <file|->     Write diagnostics to a file or stderr (default: pretty).");
        writer.WriteLine("  -o, --output <file|->                                       Output destination path (or '-' for stdout).");
        writer.WriteLine("  --color [auto|always|never]                                 Control ANSI colored diagnostics.");
        writer.WriteLine("  --diagnostic-paths (relative|project|full)                  Choose relative (CWD), project-relative, or full paths in diagnostics.");
        writer.WriteLine("  --implicit-toplevel[=true|false]                            Allow implicit top-level statements for entry files.");
        writer.WriteLine("  -Werror, --warnings-as-errors                               Treat compiler warnings as errors.");
        writer.WriteLine("  -v, --version                                               Show compiler version.");
        writer.WriteLine("  -h, --help                                                  Show this help text.");
        writer.WriteLine();
        writer.WriteLine("Source paths can be one or more '.mh' files or directories.");
        writer.WriteLine("Directories are searched recursively for matching files. '-' selects stdout for debug and stderr for diagnostics.");
    }
}