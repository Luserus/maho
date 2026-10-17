using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Miryo.Build;

namespace Miryo;

/// <summary>
/// CLI driver for Miryo, the Maho project system and build driver.
/// </summary>
public static class MiryoCommandLine
{
    public static string VersionString
    {
        get
        {
            var informational = typeof(MiryoCommandLine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                int plusIndex = informational.IndexOf('+');
                return plusIndex >= 0 ? informational[..plusIndex] : informational;
            }
            var ver = typeof(MiryoCommandLine).Assembly.GetName().Version ?? new Version(0, 1, 0);
            return ver.ToString(3);
        }
    }

    public static int Run(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
        }
        return Run(args, Console.Out, Console.Error);
    }

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            PrintUsage(stdout);
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        string[] commandArgs = args.Length > 1 ? args[1..] : [];

        switch (command)
        {
            case "-h" or "--help" or "help":
                PrintUsage(stdout);
                return 0;

            case "-v" or "--version" or "version":
                stdout.WriteLine($"Miryo: v{VersionString}");
                return 0;

            case "new":
                return ExecuteNew(commandArgs, stdout, stderr);

            case "build":
                return ExecuteBuild(commandArgs, stdout, stderr);

            case "run":
                return ExecuteRun(commandArgs, stdout, stderr);

            case "clean":
                return ExecuteClean(commandArgs, stdout, stderr);

            case "check":
                return ExecuteCheck(commandArgs, stdout, stderr);

            default:
                if (command.StartsWith('-'))
                {
                    stderr.WriteLine($"Unknown option '{command}'.");
                }
                else
                {
                    stderr.WriteLine($"Unknown command '{args[0]}'. Expected a command keyword.");
                }

                stderr.WriteLine();
                PrintUsage(stderr);
                return 1;
        }
    }

    private static int ExecuteNew(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? projectName = null;
        bool isLib = false;
        string? configPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--lib":
                    isLib = true;
                    break;
                case "--bin":
                    isLib = false;
                    break;
                case "--config":
                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                    {
                        stderr.WriteLine("The --config option requires a configuration file path.");
                        return 1;
                    }
                    configPath = args[++i];
                    break;
                case "-h" or "--help":
                    PrintNewUsage(stdout);
                    return 0;
                default:
                    if (arg.StartsWith('-'))
                    {
                        stderr.WriteLine($"Unknown option '{arg}' for 'miryo new'.");
                        return 1;
                    }
                    if (projectName is not null)
                    {
                        stderr.WriteLine("Only one project name can be specified for 'miryo new'.");
                        return 1;
                    }
                    projectName = arg;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(projectName))
        {
            stderr.WriteLine("Missing project name. Usage: miryo new <ProjectName> [--bin|--lib]");
            return 1;
        }

        // Validate project name contains valid identifier characters
        if (!char.IsLetter(projectName[0]) && projectName[0] != '_')
        {
            stderr.WriteLine($"Invalid project name '{projectName}'. Project name must start with a letter or underscore.");
            return 1;
        }

        string targetDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), projectName));
        if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
        {
            stderr.WriteLine($"Directory '{targetDir}' already exists and is not empty.");
            return 1;
        }

        try
        {
            Directory.CreateDirectory(targetDir);

            MiryoConfiguration config = MiryoConfiguration.Load(configPath);
            if (!config.IsLoadedFromDisk && !string.IsNullOrWhiteSpace(config.LoadWarning))
            {
                stdout.WriteLine(config.LoadWarning);
            }

            var templateVars = new Dictionary<string, string>
            {
                ["project_name"] = projectName
            };

            string templateKey = isLib ? "lib_template" : "bin_template";
            string projectFileContent = config.GetTemplate(templateKey, templateVars);

            string projectFilePath = Path.Combine(targetDir, $"{projectName}.mhpr");
            File.WriteAllText(projectFilePath, projectFileContent);

            string sourcePathKey = isLib ? "lib_source_path" : "bin_source_path";
            string sourceContentKey = isLib ? "lib_source" : "bin_source";

            string relSourcePath = config.GetTemplate(sourcePathKey, templateVars);
            string sourceContent = config.GetTemplate(sourceContentKey, templateVars);

            string fullSourcePath = Path.Combine(targetDir, relSourcePath);
            string? sourceDir = Path.GetDirectoryName(fullSourcePath);
            if (!string.IsNullOrEmpty(sourceDir))
            {
                Directory.CreateDirectory(sourceDir);
            }
            File.WriteAllText(fullSourcePath, sourceContent);

            string gitignoreContent = config.GetTemplate("gitignore", templateVars);
            if (!string.IsNullOrEmpty(gitignoreContent))
            {
                File.WriteAllText(Path.Combine(targetDir, ".gitignore"), gitignoreContent);
            }

            stdout.WriteLine($"Created Maho {(isLib ? "library" : "application")} project '{projectName}' successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Failed to create project '{projectName}': {ex.Message}");
            return 1;
        }
    }

    private static int ExecuteBuild(string[] args, TextWriter stdout, TextWriter stderr, bool checkOnly = false)
    {
        string? targetPath = null;
        string? configPath = null;
        bool noProject = false;
        bool stopAtIl = false;
        string? outputDir = null;
        string? colorMode = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--config":
                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                    {
                        stderr.WriteLine("The --config option requires a configuration file path.");
                        return 1;
                    }
                    configPath = args[++i];
                    break;
                case "-np" or "--no-project" or "--allow-no-project":
                    noProject = true;
                    break;
                case "--stop-at-il" or "--emit-il":
                    stopAtIl = true;
                    break;
                case "--check":
                    checkOnly = true;
                    break;
                case "--color":
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    {
                        colorMode = args[++i].ToLowerInvariant();
                    }
                    else
                    {
                        colorMode = "always";
                    }
                    break;
                case string c when c.StartsWith("--color=", StringComparison.OrdinalIgnoreCase):
                    colorMode = c[(c.IndexOf('=') + 1)..].ToLowerInvariant();
                    break;
                case "-o" or "--output":
                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                    {
                        stderr.WriteLine("The -o/--output option requires a destination path.");
                        return 1;
                    }
                    outputDir = args[++i];
                    break;
                case "-h" or "--help":
                    PrintBuildUsage(stdout);
                    return 0;
                default:
                    if (arg.StartsWith('-'))
                    {
                        stderr.WriteLine($"Unknown option '{arg}' for 'miryo build'.");
                        return 1;
                    }
                    if (targetPath is not null)
                    {
                        stderr.WriteLine("Only one target directory or project file can be specified.");
                        return 1;
                    }
                    targetPath = arg;
                    break;
            }
        }

        targetPath ??= Directory.GetCurrentDirectory();
        string fullTargetPath = Path.GetFullPath(targetPath);
        var buildTimer = Stopwatch.StartNew();

        MiryoConfiguration config;
        try
        {
            config = MiryoConfiguration.Load(configPath);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Failed to load configuration: {ex.Message}");
            return 1;
        }

        string? mahocPath = config.FindTool("mahoc");
        if (mahocPath is null)
        {
            stderr.WriteLine("error: Could not locate 'mahoc' compiler executable.");
            stderr.WriteLine($"Searched candidates: {string.Join(", ", config.GetToolCandidates("mahoc"))}");
            stderr.WriteLine("Ensure 'mahoc' is built in dist/ or configure its path in miryo.config.");
            return 1;
        }

        var mahocArgs = new List<string>();
        string displayProjectName = Path.GetFileNameWithoutExtension(fullTargetPath);
        if (!noProject && Directory.Exists(fullTargetPath))
        {
            string? foundProject = MiryoBuildSystem.FindProjectFile(fullTargetPath);
            if (foundProject is not null)
                displayProjectName = Path.GetFileNameWithoutExtension(foundProject);
        }

        string[]? explicitSourcePaths = null;
        bool useColors = ShouldUseColors(colorMode);

        mahocArgs.Add("--color");
        mahocArgs.Add(useColors ? "always" : "never");

        if (checkOnly)
            mahocArgs.Add("--check");
        else if (stopAtIl)
            mahocArgs.Add("--emit-il");

        if (outputDir is not null)
        {
            mahocArgs.Add("-o");
            mahocArgs.Add(outputDir);
        }

        stdout.WriteLine($"Building {displayProjectName}:");
        StartPipelineStage(stdout, "Analyzing project configuration...");
        var configurationTimer = Stopwatch.StartNew();

        if (noProject)
        {
            if (File.Exists(fullTargetPath) || Directory.Exists(fullTargetPath))
            {
                mahocArgs.Add(fullTargetPath);
            }
            else
            {
                configurationTimer.Stop();
                CompletePipelineStage(stdout, success: false, configurationTimer.Elapsed, useColors);
                stdout.Flush();
                stderr.WriteLine();
                stderr.WriteLine("Project:");
                stderr.WriteLine($"Input path not found: {fullTargetPath}");
                stderr.Flush();
                stdout.WriteLine();
                stdout.WriteLine($"build {FormatFailed("failed", useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
                stdout.Flush();
                return 1;
            }
        }
        else
        {
            string projectFilePath;
            if (File.Exists(fullTargetPath) && string.Equals(Path.GetExtension(fullTargetPath), ".mhpr", StringComparison.OrdinalIgnoreCase))
            {
                projectFilePath = fullTargetPath;
            }
            else if (Directory.Exists(fullTargetPath))
            {
                string? found = MiryoBuildSystem.FindProjectFile(fullTargetPath);
                if (found is null)
                {
                    configurationTimer.Stop();
                    CompletePipelineStage(stdout, success: false, configurationTimer.Elapsed, useColors);
                    stdout.Flush();
                    stderr.WriteLine();
                    stderr.WriteLine("Project:");
                    stderr.WriteLine($"No project file ('.mhpr') found in '{fullTargetPath}'. Use --no-project to compile without a project file.");
                    stderr.Flush();
                    stdout.WriteLine();
                    stdout.WriteLine($"build {FormatFailed("failed", useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
                    stdout.Flush();
                    return 1;
                }
                projectFilePath = found;
            }
            else
            {
                configurationTimer.Stop();
                CompletePipelineStage(stdout, success: false, configurationTimer.Elapsed, useColors);
                stdout.Flush();
                stderr.WriteLine();
                stderr.WriteLine("Project:");
                stderr.WriteLine($"Target path not found: {fullTargetPath}");
                stderr.Flush();
                stdout.WriteLine();
                stdout.WriteLine($"build {FormatFailed("failed", useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
                stdout.Flush();
                return 1;
            }

            MahoProject project;
            try
            {
                project = MiryoBuildSystem.LoadProject(projectFilePath);
            }
            catch (Exception ex)
            {
                configurationTimer.Stop();
                CompletePipelineStage(stdout, success: false, configurationTimer.Elapsed, useColors);
                stdout.Flush();
                stderr.WriteLine();
                stderr.WriteLine("Project:");
                stderr.WriteLine($"Failed to load project '{projectFilePath}': {ex.Message}");
                stderr.Flush();
                stdout.WriteLine();
                stdout.WriteLine($"build {FormatFailed("failed", useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
                stdout.Flush();
                return 1;
            }

            // If there are referenced projects, build them first
            foreach (string refProj in project.Configuration.ProjectsReferenced)
            {
                string refPath = Path.IsPathRooted(refProj)
                    ? refProj
                    : Path.GetFullPath(Path.Combine(project.ProjectDirectory, refProj));

                if (!File.Exists(refPath) && !Directory.Exists(refPath))
                {
                    configurationTimer.Stop();
                    CompletePipelineStage(stdout, success: false, configurationTimer.Elapsed, useColors);
                    stdout.Flush();
                    stderr.WriteLine();
                    stderr.WriteLine("Project:");
                    stderr.WriteLine($"Referenced project not found: {refPath}");
                    stderr.Flush();
                    stdout.WriteLine();
                    stdout.WriteLine($"build {FormatFailed("failed", useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
                    stdout.Flush();
                    return 1;
                }

                var refArgs = new List<string> { refPath };
                if (colorMode is not null)
                {
                    refArgs.Add("--color");
                    refArgs.Add(colorMode);
                }
                int refExit = ExecuteBuild([.. refArgs], stdout, stderr, checkOnly);
                if (refExit != 0)
                    return refExit;
            }

            if (project.EntryFile is not null)
            {
                mahocArgs.Add("--entry");
                mahocArgs.Add(project.EntryFile);
            }

            mahocArgs.Add($"--implicit-toplevel={(project.ImplicitTopLevel ? "true" : "false")}");

            if (project.GlobalUnsafeEnabled)
            {
                mahocArgs.Add("--unsafe");
            }

            foreach (var (aliasName, target) in project.GlobalAliases)
            {
                mahocArgs.Add("--alias");
                mahocArgs.Add($"{aliasName}={target}");
            }

            foreach (string referencedProject in project.ProjectsReferenced)
            {
                string referencePath = Path.IsPathRooted(referencedProject)
                    ? referencedProject
                    : Path.GetFullPath(Path.Combine(project.ProjectDirectory, referencedProject));
                mahocArgs.Add("--reference-project");
                mahocArgs.Add(referencePath);
            }

            mahocArgs.Add("--project-name");
            mahocArgs.Add(project.ProjectName);
            mahocArgs.Add("--module-name");
            mahocArgs.Add(ToKebabCase(project.ProjectName));
            mahocArgs.Add("--project-directory");
            mahocArgs.Add(project.ProjectDirectory);
            mahocArgs.Add("--source-paths-stdin");
            explicitSourcePaths = [.. project.SourceFiles];
        }

        // The configured server selects the compiler package. Without one,
        // mahoc uses only its colocated-server convenience fallback.
        string? serverPath = config.FindTool("mahoc_server");
        if (serverPath is not null)
        {
            mahocArgs.Add("--server-path");
            mahocArgs.Add(serverPath);
        }

        configurationTimer.Stop();
        CompletePipelineStage(stdout, success: true, configurationTimer.Elapsed, useColors);

        StartPipelineStage(stdout, "Executing core compile...");
        var compileTimer = Stopwatch.StartNew();

        try
        {
            var psi = MiryoConfiguration.CreateProcessStartInfo(mahocPath, mahocArgs);
            psi.RedirectStandardInput = explicitSourcePaths is not null;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            string resultPipeName = $"miryo-result-{Guid.NewGuid():N}";
            psi.ArgumentList.Add("--result-pipe");
            psi.ArgumentList.Add(resultPipeName);

            using var resultPipe = new NamedPipeServerStream(resultPipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            using var process = Process.Start(psi);
            if (process is null)
            {
                compileTimer.Stop();
                CompletePipelineStage(stdout, success: false, compileTimer.Elapsed, useColors);
                stdout.Flush();
                stderr.WriteLine();
                stderr.WriteLine("Compiler:");
                stderr.WriteLine("Failed to launch 'mahoc' process.");
                stderr.Flush();
                stdout.WriteLine();
                stdout.WriteLine($"build {FormatFailed("failed", useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
                stdout.Flush();
                return 1;
            }

            if (explicitSourcePaths is not null)
            {
                foreach (string sourcePath in explicitSourcePaths)
                    process.StandardInput.WriteLine(sourcePath);
                process.StandardInput.Close();
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var diagnosticsTask = process.StandardError.ReadToEndAsync();
            using var pipeTimeout = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));
            Task pipeConnection = resultPipe.WaitForConnectionAsync(pipeTimeout.Token);
            Task processExit = process.WaitForExitAsync();
            if (Task.WhenAny(pipeConnection, processExit).GetAwaiter().GetResult() == processExit)
            {
                string compilerDiagnostics = diagnosticsTask.GetAwaiter().GetResult();
                throw new InvalidDataException($"mahoc exited before writing its result pipe (exit code {process.ExitCode}). {compilerDiagnostics}".TrimEnd());
            }
            pipeConnection.GetAwaiter().GetResult();
            using var resultReader = new StreamReader(resultPipe, Encoding.UTF8, leaveOpen: true);
            string resultJson = resultReader.ReadToEnd();
            process.WaitForExit();

            compileTimer.Stop();
            string compilerOutput = outputTask.GetAwaiter().GetResult();
            string diagnostics = diagnosticsTask.GetAwaiter().GetResult();

            long elapsedTicks = compileTimer.Elapsed.Ticks;
            bool hasErrors = process.ExitCode != 0;
            bool laterStagesNotImplemented = false;
            if (!string.IsNullOrWhiteSpace(resultJson))
            {
                using JsonDocument result = JsonDocument.Parse(resultJson);
                var root = result.RootElement;
                if (root.TryGetProperty("elapsedTicks", out JsonElement ticks))
                    elapsedTicks = ticks.GetInt64();
                if (root.TryGetProperty("hasErrors", out JsonElement errors))
                    hasErrors = errors.GetBoolean();
                laterStagesNotImplemented = root.TryGetProperty("pipelineError", out JsonElement pipeline) &&
                    pipeline.ValueKind == JsonValueKind.String &&
                    pipeline.GetString()?.Contains("lowering and code-generation", StringComparison.OrdinalIgnoreCase) == true;
            }

            TimeSpan compileElapsed = TimeSpan.FromTicks(elapsedTicks);
            CompletePipelineStage(stdout, success: !hasErrors, compileElapsed, useColors);

            if (!string.IsNullOrEmpty(compilerOutput))
            {
                stdout.Write(compilerOutput);
                stdout.Flush();
            }

            if (!string.IsNullOrWhiteSpace(diagnostics))
            {
                stdout.Flush();
                stderr.WriteLine();
                stderr.WriteLine("Compiler:");
                stderr.WriteLine(diagnostics.TrimEnd());
                stderr.Flush();
            }

            if (laterStagesNotImplemented)
            {
                stdout.WriteLine($"build {FormatFailed("failed", useColors)} due to later stages not being implemented");
            }

            stdout.WriteLine();
            stdout.WriteLine($"build {FormatStatus(process.ExitCode == 0, useColors, capitalize: false)} in {FormatElapsed(buildTimer.Elapsed)}");
            stdout.Flush();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            compileTimer.Stop();
            CompletePipelineStage(stdout, success: false, compileTimer.Elapsed, useColors);
            stdout.Flush();
            stderr.WriteLine();
            stderr.WriteLine("Compiler:");
            stderr.WriteLine($"Error launching compiler: {ex.Message}");
            stderr.Flush();
            stdout.WriteLine();
            stdout.WriteLine($"build {FormatFailed("failed", useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
            stdout.Flush();
            return 1;
        }
    }

    private static string ToKebabCase(string value)
    {
        var result = new StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]) && (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1])))
                result.Append('-');
            result.Append(char.ToLowerInvariant(value[i]));
        }
        return result.ToString();
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds >= 1)
            return $"{elapsed.TotalSeconds:0.##}s";
        if (elapsed.TotalMilliseconds >= 1)
            return $"{elapsed.TotalMilliseconds:0.##}ms";
        if (elapsed.TotalMicroseconds >= 1)
            return $"{elapsed.TotalMicroseconds:0.##}µs";
        return $"{elapsed.Ticks * 100:n0}ns";
    }

    private static int ExecuteRun(string[] args, TextWriter stdout, TextWriter stderr)
    {
        // Split args before and after "--"
        string[] buildArgs;
        string[] runtimeArgs;

        int dashDashIdx = Array.IndexOf(args, "--");
        if (dashDashIdx >= 0)
        {
            buildArgs = args[..dashDashIdx];
            runtimeArgs = args[(dashDashIdx + 1)..];
        }
        else
        {
            buildArgs = args;
            runtimeArgs = [];
        }

        int buildExit = ExecuteBuild(buildArgs, stdout, stderr, checkOnly: false);
        if (buildExit != 0)
        {
            return buildExit;
        }

        bool useColors = ShouldUseColors(null);
        stderr.WriteLine($"Run {FormatFailed("failed", useColors)}: Native code execution pipeline not implemented yet.");
        return 1;
    }

    private static int ExecuteCheck(string[] args, TextWriter stdout, TextWriter stderr) =>
        ExecuteBuild(args, stdout, stderr, checkOnly: true);

    private static int ExecuteClean(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string targetDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
        if (!Directory.Exists(targetDir))
        {
            stderr.WriteLine($"Directory not found: {targetDir}");
            return 1;
        }

        string[] cleanTargets = ["target", "dist", "bin", "obj"];
        int cleanedCount = 0;

        foreach (string target in cleanTargets)
        {
            string dir = Path.Combine(targetDir, target);
            if (Directory.Exists(dir))
            {
                try
                {
                    Directory.Delete(dir, true);
                    cleanedCount++;
                }
                catch (Exception ex)
                {
                    stderr.WriteLine($"Failed to delete '{dir}': {ex.Message}");
                }
            }
        }

        stdout.WriteLine($"Cleaned {cleanedCount} build artifact directories.");
        return 0;
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine($"Miryo: v{VersionString}");
        writer.WriteLine("The project system and build tool for the Maho programming language.");
        writer.WriteLine();
        writer.WriteLine("Usage: miryo <command> [options]");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        writer.WriteLine("  new <name>       Create a new Maho project scaffolding (--bin or --lib)");
        writer.WriteLine("  build [path]     Compile the project or directory");
        writer.WriteLine("  check [path]     Verify syntax and semantics without code generation");
        writer.WriteLine("  run [path]       Build and execute the program");
        writer.WriteLine("  clean [path]     Remove build artifact directories (target/, obj/, dist/)");
        writer.WriteLine();
        writer.WriteLine("General Options:");
        writer.WriteLine("  --config <path>  Specify toolchain configuration file (default: miryo.config)");
        writer.WriteLine("  -v, --version    Show Miryo version");
        writer.WriteLine("  -h, --help       Show this help message");
    }

    private static void PrintNewUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: miryo new <ProjectName> [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --bin            Create an application project (default)");
        writer.WriteLine("  --lib            Create a library project");
        writer.WriteLine("  --config <path>  Specify configuration file for project templates");
        writer.WriteLine("  -h, --help       Show this help text");
    }

    private static void PrintBuildUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: miryo build [path] [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  -np, --no-project     Compile directory source files without requiring a .mhpr file");
        writer.WriteLine("  --stop-at-il          Compile up to Maho Intermediate Language (IL) and halt");
        writer.WriteLine("  --check               Verify syntax and semantics without code generation");
        writer.WriteLine("  -o, --output <dir>    Set output directory destination");
        writer.WriteLine("  --color <mode>        Control ANSI colored diagnostics (auto, always, never)");
        writer.WriteLine("  --config <path>       Specify toolchain configuration file (default: miryo.config)");
        writer.WriteLine("  -h, --help            Show this help text");
    }

    private const string AnsiReset = "\u001b[0m";
    private const string AnsiRed = "\u001b[31;1m";
    private const string AnsiGreen = "\u001b[32;1m";

    private static bool ShouldUseColors(string? colorMode) => colorMode switch
    {
        "always" => true,
        "never" => false,
        _ => (!Console.IsOutputRedirected || !Console.IsErrorRedirected) &&
             Environment.GetEnvironmentVariable("NO_COLOR") == null &&
             Environment.GetEnvironmentVariable("TERM") != "dumb"
    };

    private static string FormatSuccess(string text, bool useColors) => useColors ? $"{AnsiGreen}{text}{AnsiReset}" : text;
    private static string FormatFailed(string text, bool useColors) => useColors ? $"{AnsiRed}{text}{AnsiReset}" : text;

    private static string FormatStatus(bool success, bool useColors, bool capitalize = true)
    {
        string text = success ? (capitalize ? "Success" : "success") : (capitalize ? "Failed" : "failed");
        return success ? FormatSuccess(text, useColors) : FormatFailed(text, useColors);
    }

    private static void StartPipelineStage(TextWriter writer, string stage)
    {
        const int ColumnWidth = 42;
        string paddedStage = stage.PadRight(ColumnWidth);
        writer.Write(paddedStage);
        writer.Flush();
    }

    private static void CompletePipelineStage(TextWriter writer, bool success, TimeSpan elapsed, bool useColors)
    {
        string status = success
            ? FormatSuccess("Success", useColors)
            : $"{FormatFailed("Failed", useColors)} ";
        writer.WriteLine($"{status} | {FormatElapsed(elapsed)}");
        writer.Flush();
    }
}
