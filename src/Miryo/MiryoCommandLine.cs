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
using Miryo.Toolchain;

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

            var ver = typeof(MiryoCommandLine).Assembly.GetName().Version ?? new Version(1, 0, 0);
            return ver.ToString(3);
        }
    }

    public static string ToolchainVersion => Miryo.Toolchain.MahoPaths.NormalizeVersion(VersionString);

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

        var toolchain = new LocalToolchainManager();
        var invocation = MiryoInvocation.Parse(args, defaultVersion: toolchain.ActiveVersion);

        switch (invocation.Command)
        {
            case "-h" or "--help" or "help":
                PrintUsage(stdout);
                return 0;

            case "-v" or "--version" or "version":
                stdout.WriteLine(VersionString);
                return 0;

            case "compile":
                return ExecuteCompile(invocation, toolchain, stdout, stderr);

            case "build":
                return ExecuteBuild(invocation.Arguments, stdout, stderr, checkOnly: false, invocation: invocation, toolchain: toolchain);

            case "run":
                return ExecuteRun(invocation.Arguments, stdout, stderr, invocation: invocation, toolchain: toolchain);

            case "test":
                return ExecuteTest(invocation, toolchain, stdout, stderr);

            case "ls":
                return ExecuteLs(invocation, toolchain, stdout, stderr);

            case "tool":
                return ExecuteTool(invocation, toolchain, stdout, stderr);

            case "update":
                return ExecuteUpdate(invocation, toolchain, stdout, stderr);

            case "new":
                return ExecuteNew(invocation.Arguments, stdout, stderr, invocation);

            case "clean":
                return ExecuteClean(invocation.Arguments, stdout, stderr, invocation);

            case "compiler-server":
                return ExecuteCompilerServer(invocation.Arguments, stdout, stderr, invocation);

            default:
                bool useColors = ShouldUseColors(invocation.ColorMode);

                if (invocation.Command.StartsWith('-'))
                    stderr.WriteLine($"{FormatError("error", useColors)}: unknown option '{invocation.Command}'.");
                else
                    stderr.WriteLine($"{FormatError("error", useColors)}: unknown command '{invocation.Command}'. Expected a command keyword.");

                stderr.WriteLine();
                PrintUsage(stderr);

                return 1;
        }
    }

    private static int ExecuteNew(string[] args, TextWriter stdout, TextWriter stderr, MiryoInvocation? invocation = null)
    {
        string? projectName = null;
        bool isLib = false;
        string? configPath = null;
        string? colorMode = invocation?.ColorMode;
        bool useColors = ShouldUseColors(colorMode);

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
                        stderr.WriteLine($"{FormatError("error", useColors)}: the --config option requires a configuration file path.");
                        return 1;
                    }
                    configPath = args[++i];
                    break;
                case "--color":
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        colorMode = args[++i].ToLowerInvariant();
                    else
                        colorMode = "always";
                    useColors = ShouldUseColors(colorMode);
                    break;
                case string c when c.StartsWith("--color=", StringComparison.OrdinalIgnoreCase):
                    colorMode = c[(c.IndexOf('=') + 1)..].ToLowerInvariant();
                    useColors = ShouldUseColors(colorMode);
                    break;
                case "-h" or "--help":
                    PrintNewUsage(stdout);
                    return 0;
                default:
                    if (arg.StartsWith('-'))
                    {
                        stderr.WriteLine($"{FormatError("error", useColors)}: unknown option '{arg}' for 'miryo new'.");
                        return 1;
                    }
                    if (projectName is not null)
                    {
                        stderr.WriteLine($"{FormatError("error", useColors)}: only one project name can be specified for 'miryo new'.");
                        return 1;
                    }
                    projectName = arg;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(projectName))
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: missing project name. Usage: miryo new <ProjectName> [--bin|--lib]");
            return 1;
        }

        // Validate project name contains valid identifier characters
        if (!char.IsLetter(projectName[0]) && projectName[0] != '_')
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: invalid project name '{projectName}'. Project name must start with a letter or underscore.");
            return 1;
        }

        string targetDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), projectName));

        if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: directory '{targetDir}' already exists and is not empty.");
            return 1;
        }

        try
        {
            Directory.CreateDirectory(targetDir);

            MiryoConfiguration config = MiryoConfiguration.Load(configPath);
            if (!config.IsLoadedFromDisk && !string.IsNullOrWhiteSpace(config.LoadWarning))
                stdout.WriteLine(config.LoadWarning);

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
                File.WriteAllText(Path.Combine(targetDir, ".gitignore"), gitignoreContent);

            stdout.WriteLine($"Created Maho {(isLib ? "library" : "application")} project '{projectName}' successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: failed to create project '{projectName}': {ex.Message}");
            return 1;
        }
    }

    private static int ExecuteBuild(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        bool checkOnly = false,
        MiryoInvocation? invocation = null,
        IToolchainManager? toolchain = null)
    {
        string? targetPath = null;
        string? configPath = null;
        bool noProject = false;
        bool stopAtIl = false;
        string? outputDir = null;
        string? colorMode = null;
        bool buildAll = false;
        var targetProjects = new List<string>();
        var excludedProjects = new List<string>();
        bool useColors = ShouldUseColors(colorMode);

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg)
            {
                case "--config":
                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                    {
                        stderr.WriteLine($"{FormatError("error", useColors)}: the --config option requires a configuration file path.");
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
                case "-p" or "--project":
                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                    {
                        stderr.WriteLine($"{FormatError("error", useColors)}: the -p/--project option requires a project name.");
                        return 1;
                    }

                    string projArg = args[++i];
                    foreach (var p in projArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        targetProjects.Add(p);
                    break;
                case string p when p.StartsWith("--project=", StringComparison.OrdinalIgnoreCase):
                    string pVal = p[(p.IndexOf('=') + 1)..];
                    foreach (var proj in pVal.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        targetProjects.Add(proj);
                    break;
                case "--all":
                    buildAll = true;
                    break;
                case "--exclude":
                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                    {
                        stderr.WriteLine($"{FormatError("error", useColors)}: the --exclude option requires a project name.");
                        return 1;
                    }

                    string exclArg = args[++i];
                    foreach (var p in exclArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        excludedProjects.Add(p);
                    break;
                case string e when e.StartsWith("--exclude=", StringComparison.OrdinalIgnoreCase):
                    string eVal = e[(e.IndexOf('=') + 1)..];
                    foreach (var proj in eVal.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        excludedProjects.Add(proj);
                    break;
                case "--color":
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        colorMode = args[++i].ToLowerInvariant();
                    else
                        colorMode = "always";

                    useColors = ShouldUseColors(colorMode);
                    break;
                case string c when c.StartsWith("--color=", StringComparison.OrdinalIgnoreCase):
                    colorMode = c[(c.IndexOf('=') + 1)..].ToLowerInvariant();
                    useColors = ShouldUseColors(colorMode);
                    break;
                case "-o" or "--output":
                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                    {
                        stderr.WriteLine($"{FormatError("error", useColors)}: the -o/--output option requires a destination path.");
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
                        stderr.WriteLine($"{FormatError("error", useColors)}: unknown option '{arg}' for 'miryo build'.");
                        return 1;
                    }
                    if (targetPath is not null)
                    {
                        stderr.WriteLine($"{FormatError("error", useColors)}: only one target directory or project file can be specified.");
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
            stderr.WriteLine($"{FormatError("error", useColors)}: failed to load configuration: {ex.Message}");
            return 1;
        }

        toolchain ??= new LocalToolchainManager();

        if (!noProject)
        {
            var wsOrProj = MiryoBuildSystem.FindWorkspaceOrProject(fullTargetPath);
            if (wsOrProj is { IsWorkspace: true } wsInfo)
            {
                return ExecuteWorkspaceBuild(
                    wsInfo.FilePath,
                    targetProjects,
                    buildAll,
                    excludedProjects,
                    outputDir,
                    checkOnly,
                    stopAtIl,
                    colorMode,
                    config,
                    toolchain,
                    invocation,
                    stdout,
                    stderr);
            }

            if (targetProjects.Count > 0 || buildAll || excludedProjects.Count > 0)
            {
                stderr.WriteLine($"{FormatError("error", useColors)}: options -p/--project, --all, and --exclude can only be used when building a workspace.");
                return 1;
            }
        }
        string targetVersion = invocation?.TargetVersion ?? toolchain.ActiveVersion;
        string? mahocPath = toolchain.FindTool("mahoc", targetVersion) ?? config.FindTool("mahoc");

        if (mahocPath is null)
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: Could not locate 'mahoc' compiler executable.");
            stderr.WriteLine($"searched candidates: {string.Join(", ", config.GetToolCandidates("mahoc"))}");
            stderr.WriteLine("ensure 'mahoc' is built in bin/maho/ or configure its path in miryo.config.");

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
        useColors = ShouldUseColors(colorMode);

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

        stdout.WriteLine($"Building '{displayProjectName}'");
        stdout.WriteLine($"From: {fullTargetPath}");
        stdout.WriteLine();
        StartPipelineStage(stdout, "Analyzing project configuration...");
        var configurationTimer = Stopwatch.StartNew();

        if (noProject)
        {
            if (File.Exists(fullTargetPath) || Directory.Exists(fullTargetPath))
                mahocArgs.Add(fullTargetPath);
            else
            {
                configurationTimer.Stop();
                CompletePipelineStage(stdout, success: false, configurationTimer.Elapsed, useColors);
                stdout.Flush();
                stderr.WriteLine();
                stderr.WriteLine("project:");
                stderr.WriteLine($"{FormatError("error", useColors)}: input path not found: {fullTargetPath}");
                stderr.Flush();
                stdout.WriteLine();
                stdout.WriteLine($"build {FormatFailed("failed", useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
                stdout.WriteLine();
                stdout.WriteLine("errors: 1, warnings: 0");
                stdout.Flush();
                return 1;
            }
        }
        else
        {
            string projectFilePath;

            if (File.Exists(fullTargetPath) && string.Equals(Path.GetExtension(fullTargetPath), ".mhpr", StringComparison.OrdinalIgnoreCase))
                projectFilePath = fullTargetPath;
            else if (Directory.Exists(fullTargetPath))
            {
                string? found = MiryoBuildSystem.FindProjectFile(fullTargetPath);

                if (found is null)
                {
                    configurationTimer.Stop();
                    CompletePipelineStage(stdout, success: false, configurationTimer.Elapsed, useColors);
                    stdout.Flush();
                    stderr.WriteLine();
                    stderr.WriteLine("project:");
                    stderr.WriteLine($"{FormatError("error", useColors)}: no project file ('.mhpr') found in '{fullTargetPath}'. Use --no-project to compile without a project file.");
                    stderr.Flush();
                    stdout.WriteLine();
                    stdout.WriteLine($"build {FormatFailed("failed", useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
                    stdout.WriteLine();
                    stdout.WriteLine("errors: 1, warnings: 0");
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
                stderr.WriteLine("project:");
                stderr.WriteLine($"{FormatError("error", useColors)}: input path not found: {fullTargetPath}");
                stderr.Flush();
                stdout.WriteLine();
                stdout.WriteLine($"build {FormatFailed("failed", useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
                stdout.WriteLine();
                stdout.WriteLine("errors: 1, warnings: 0");
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
                stderr.WriteLine("project:");
                stderr.WriteLine($"{FormatError("error", useColors)}: failed to load project '{projectFilePath}': {ex.Message}");
                stderr.Flush();
                stdout.WriteLine();
                stdout.WriteLine($"build {FormatFailed("failed", useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
                stdout.WriteLine();
                stdout.WriteLine("errors: 1, warnings: 0");
                stdout.Flush();

                return 1;
            }

            // If there are referenced projects, build them first
            foreach (string refSpec in project.Configuration.ProjectsReferenced)
            {
                MiryoBuildSystem.ParseReferenceSpec(refSpec, out _, out string refProj);
                string refPath = Path.IsPathRooted(refProj)
                    ? refProj
                    : Path.GetFullPath(Path.Combine(project.ProjectDirectory, refProj));

                if (!File.Exists(refPath) && !Directory.Exists(refPath))
                {
                    configurationTimer.Stop();
                    CompletePipelineStage(stdout, success: false, configurationTimer.Elapsed, useColors);
                    stdout.Flush();
                    stderr.WriteLine();
                    stderr.WriteLine("project:");
                    stderr.WriteLine($"{FormatError("error", useColors)}: referenced project not found: {refPath}");
                    stderr.Flush();
                    stdout.WriteLine();
                    stdout.WriteLine($"build {FormatFailed("failed", useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
                    stdout.WriteLine();
                    stdout.WriteLine("errors: 1, warnings: 0");
                    stdout.Flush();

                    return 1;
                }

                var refArgs = new List<string> { refPath };

                if (colorMode is not null)
                {
                    refArgs.Add("--color");
                    refArgs.Add(colorMode);
                }

                int refExit = ExecuteBuild([.. refArgs], stdout, stderr, checkOnly, invocation, toolchain);

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
                mahocArgs.Add("--unsafe");

            foreach (var (aliasName, target) in project.GlobalAliases)
            {
                mahocArgs.Add("--alias");
                mahocArgs.Add($"{aliasName}={target}");
            }

            foreach (string refSpec in project.ProjectsReferenced)
            {
                MiryoBuildSystem.ParseReferenceSpec(refSpec, out string? refAlias, out string referencedProject);
                string referencePath = Path.IsPathRooted(referencedProject)
                    ? referencedProject
                    : Path.GetFullPath(Path.Combine(project.ProjectDirectory, referencedProject));
                mahocArgs.Add("--reference-project");
                mahocArgs.Add(refAlias is not null ? $"{refAlias}={referencePath}" : referencePath);
            }

            mahocArgs.Add("--project-name");
            mahocArgs.Add(project.ProjectName);
            mahocArgs.Add("--module-name");
            mahocArgs.Add(project.ModuleName);
            mahocArgs.Add("--project-directory");
            mahocArgs.Add(project.ProjectDirectory);
            mahocArgs.Add("--source-paths-stdin");
            explicitSourcePaths = [.. project.SourceFiles];
        }

        // Both tools and compiler packages are configurable. The compiler is
        // a directory rather than a PATH command because its exact version is
        // part of the selected build toolchain.
        string? serverPath = config.FindTool("mahoc_server");

        if (serverPath is not null)
        {
            mahocArgs.Add("--server-path");
            mahocArgs.Add(serverPath);
        }

        string? compilerPackagePath = config.FindDirectory("mahoc_compiler");

        if (compilerPackagePath is not null)
        {
            mahocArgs.Add("--compiler-package");
            mahocArgs.Add(compilerPackagePath);
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
                stderr.WriteLine("compiler:");
                stderr.WriteLine($"{FormatError("error", useColors)}: failed to launch 'mahoc' process.");
                stderr.Flush();
                stdout.WriteLine();
                stdout.WriteLine($"build {FormatFailed("failed", useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
                stdout.WriteLine();
                stdout.WriteLine("errors: 1, warnings: 0");
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
                throw new InvalidDataException($"{FormatError("error", useColors)}: mahoc exited before writing its result pipe (exit code {process.ExitCode}). {compilerDiagnostics}".TrimEnd());
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
            int errorCount = hasErrors ? 1 : 0;
            int warningCount = 0;
            bool laterStagesNotImplemented = false;

            if (!string.IsNullOrWhiteSpace(resultJson))
            {
                using JsonDocument result = JsonDocument.Parse(resultJson);
                var root = result.RootElement;

                if (root.TryGetProperty("elapsedTicks", out JsonElement ticks))
                    elapsedTicks = ticks.GetInt64();

                if (root.TryGetProperty("hasErrors", out JsonElement errors))
                    hasErrors = errors.GetBoolean();

                if (root.TryGetProperty("errorCount", out JsonElement ec))
                    errorCount = ec.GetInt32();
                else if (hasErrors)
                    errorCount = Math.Max(1, errorCount);

                if (root.TryGetProperty("warningCount", out JsonElement wc))
                    warningCount = wc.GetInt32();

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
                stderr.WriteLine("compiler:");
                stderr.WriteLine(diagnostics.TrimEnd());
                stderr.Flush();
            }

            if (laterStagesNotImplemented)
                stdout.WriteLine($"build {FormatFailed("failed", useColors)} due to later stages not being implemented");

            bool buildSuccessful = process.ExitCode == 0 && !laterStagesNotImplemented;
            stdout.WriteLine();
            stdout.WriteLine($"build {FormatStatus(buildSuccessful, useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
            stdout.WriteLine();
            stdout.WriteLine($"errors: {errorCount}, warnings: {warningCount}");
            stdout.Flush();

            return buildSuccessful ? 0 : 1;
        }
        catch (Exception ex)
        {
            compileTimer.Stop();
            CompletePipelineStage(stdout, success: false, compileTimer.Elapsed, useColors);
            stdout.Flush();
            stderr.WriteLine();
            stderr.WriteLine("compiler:");
            stderr.WriteLine($"{FormatError("error", useColors)}: failed launching compiler: {ex.Message}");
            stderr.Flush();
            stdout.WriteLine();
            stdout.WriteLine($"build {FormatFailed("failed", useColors)} in {FormatElapsed(buildTimer.Elapsed)}");
            stdout.WriteLine();
            stdout.WriteLine("errors: 1, warnings: 0");
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
            return $"{elapsed.TotalSeconds:0.##}\u00A0s";
        if (elapsed.TotalMilliseconds >= 1)
            return $"{elapsed.TotalMilliseconds:0.##}\u00A0ms";
        if (elapsed.TotalMicroseconds >= 1)
            return $"{elapsed.TotalMicroseconds:0.##}\u00A0µs";

        return $"{elapsed.Ticks * 100:n0}\u00A0ns";
    }

    private static string? ExtractColorMode(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--color" && i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                return args[i + 1].ToLowerInvariant();
            if (args[i] == "--color")
                return "always";
            if (args[i].StartsWith("--color=", StringComparison.OrdinalIgnoreCase))
                return args[i][(args[i].IndexOf('=') + 1)..].ToLowerInvariant();
        }
        return null;
    }

    private static int ExecuteWorkspaceBuild(
        string workspaceFilePath,
        List<string> targetProjects,
        bool buildAll,
        List<string> excludedProjects,
        string? outputDir,
        bool checkOnly,
        bool stopAtIl,
        string? colorMode,
        MiryoConfiguration config,
        IToolchainManager toolchain,
        MiryoInvocation? invocation,
        TextWriter stdout,
        TextWriter stderr)
    {
        bool useColors = ShouldUseColors(colorMode);
        var workspaceTimer = Stopwatch.StartNew();

        MiryoWorkspace workspace;
        try
        {
            workspace = MiryoBuildSystem.LoadWorkspace(workspaceFilePath, outputDir);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: failed to load workspace '{workspaceFilePath}': {ex.Message}");
            return 1;
        }

        string targetVersion = invocation?.TargetVersion ?? toolchain.ActiveVersion;
        string? mahocPath = toolchain.FindTool("mahoc", targetVersion) ?? config.FindTool("mahoc");

        if (mahocPath is null)
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: Could not locate 'mahoc' compiler executable.");
            stderr.WriteLine($"searched candidates: {string.Join(", ", config.GetToolCandidates("mahoc"))}");
            stderr.WriteLine("ensure 'mahoc' is built in bin/maho/ or configure its path in miryo.config.");
            return 1;
        }

        string? serverPath = config.FindTool("mahoc_server");
        string? compilerPackagePath = config.FindDirectory("mahoc_compiler");

        stdout.WriteLine($"Building workspace '{workspace.Name}' ({workspace.Projects.Count} projects)");
        stdout.WriteLine($"From: {workspace.WorkspaceFilePath}");
        stdout.WriteLine();

        // Validate explicit target projects exist
        var missingTargets = new List<string>();
        foreach (var name in targetProjects)
        {
            if (workspace.DependencyGraph.FindProject(name) is null)
                missingTargets.Add(name);
        }

        if (missingTargets.Count > 0)
        {
            string available = string.Join(", ", workspace.Projects.Select(p => p.ProjectName));
            stderr.WriteLine($"{FormatError("error", useColors)}: project{(missingTargets.Count == 1 ? string.Empty : "s")} '{string.Join("', '", missingTargets)}' not found in workspace '{workspace.Name}'. Available projects: {available}");
            return 1;
        }

        // Determine targets to build
        List<string> targets;
        if (targetProjects.Count > 0)
        {
            targets = targetProjects;
        }
        else if (buildAll)
        {
            targets = workspace.Projects.Select(p => p.ProjectName).ToList();
        }
        else if (workspace.Configuration.Default.Length > 0)
        {
            targets = workspace.Configuration.Default.ToList();
        }
        else
        {
            targets = workspace.Projects.Select(p => p.ProjectName).ToList();
        }

        // Apply CLI exclusions
        var excludedSet = new HashSet<string>(excludedProjects, StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<IReadOnlyList<MahoProject>> tiers;
        try
        {
            tiers = workspace.DependencyGraph.GetExecutionPlan(targets);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: {ex.Message}");
            return 1;
        }

        // Filter out excluded projects from tiers
        if (excludedSet.Count > 0)
        {
            tiers = tiers
                .Select(tier => (IReadOnlyList<MahoProject>)tier.Where(p => !excludedSet.Contains(p.ProjectName)).ToList())
                .Where(tier => tier.Count > 0)
                .ToList();
        }

        var plan = new List<(MahoProject Project, int TierIndex)>();
        for (int t = 0; t < tiers.Count; t++)
        {
            foreach (var proj in tiers[t])
            {
                plan.Add((proj, t));
            }
        }

        int totalPlanCount = plan.Count;
        int totalWorkspaceProjects = workspace.Projects.Count;
        int skippedCount = totalWorkspaceProjects - totalPlanCount;
        int succeededCount = 0;
        int failedCount = 0;
        int totalErrors = 0;
        int totalWarnings = 0;

        var failedProjects = new HashSet<MahoProject>();
        var projectDiagnostics = new List<(MahoProject Project, string Diagnostics, int Errors, int Warnings)>();

        for (int i = 0; i < plan.Count; i++)
        {
            int step = i + 1;
            var (project, tierIdx) = plan[i];

            // Check if any transitive dependency failed
            var deps = workspace.DependencyGraph.GetTransitiveDependencies(project);
            var failedDep = deps.FirstOrDefault(d => failedProjects.Contains(d));
            if (failedDep != null)
            {
                stdout.WriteLine($"[{step}/{totalPlanCount}] Skipping '{project.ProjectName}' (Tier {tierIdx}) due to failure in '{failedDep.ProjectName}'");
                stdout.WriteLine();
                skippedCount++;
                failedProjects.Add(project);
                continue;
            }

            stdout.WriteLine($"[{step}/{totalPlanCount}] Building '{project.ProjectName}' (Tier {tierIdx})...");
            StartPipelineStage(stdout, "Analyzing project configuration...");
            var configTimer = Stopwatch.StartNew();
            bool configValid = true;
            string? configError = null;

            if (!Directory.Exists(project.ProjectDirectory))
            {
                configValid = false;
                configError = $"Project directory not found: {project.ProjectDirectory}";
            }
            else
            {
                foreach (var file in project.SourceFiles)
                {
                    if (!File.Exists(file))
                    {
                        configValid = false;
                        configError = $"Source file not found: {file}";
                        break;
                    }
                }
            }

            configTimer.Stop();
            CompletePipelineStage(stdout, success: configValid, configTimer.Elapsed, useColors);

            if (!configValid)
            {
                stdout.WriteLine($"Errors: {FormatFailed("1", useColors)}, Warnings: 0");
                stdout.WriteLine();
                failedCount++;
                totalErrors++;
                failedProjects.Add(project);
                projectDiagnostics.Add((project, $"error: {configError}", 1, 0));
                continue;
            }

            StartPipelineStage(stdout, "Executing core compile...");
            var compileResult = CompileProject(
                mahocPath,
                project,
                workspace.TargetDirectory,
                checkOnly,
                stopAtIl,
                useColors,
                serverPath,
                compilerPackagePath);

            CompletePipelineStage(stdout, success: compileResult.Success, compileResult.CompileElapsed, useColors);

            string errorText = compileResult.ErrorCount > 0 ? FormatFailed(compileResult.ErrorCount.ToString(), useColors) : "0";
            string warnText = compileResult.WarningCount > 0 ? (useColors ? $"{AnsiYellow}{compileResult.WarningCount}{AnsiReset}" : compileResult.WarningCount.ToString()) : "0";
            stdout.WriteLine($"Errors: {errorText}, Warnings: {warnText}");
            stdout.WriteLine();

            totalErrors += compileResult.ErrorCount;
            totalWarnings += compileResult.WarningCount;

            if (!compileResult.Success)
            {
                failedCount++;
                failedProjects.Add(project);
            }
            else
            {
                succeededCount++;
            }

            if (!string.IsNullOrWhiteSpace(compileResult.Diagnostics))
            {
                projectDiagnostics.Add((project, compileResult.Diagnostics, compileResult.ErrorCount, compileResult.WarningCount));
            }
        }

        if (projectDiagnostics.Count > 0)
        {
            stdout.Flush();
            foreach (var (proj, diags, errCount, warnCount) in projectDiagnostics)
            {
                stderr.WriteLine();
                string header = errCount > 0
                    ? $"compiler errors in '{proj.ProjectFilePath}':"
                    : $"compiler warnings in '{proj.ProjectFilePath}':";
                stderr.WriteLine(header);
                stderr.WriteLine(diags.TrimEnd());
            }
            stderr.Flush();
        }

        bool allSuccess = failedCount == 0;
        string statusText = allSuccess ? "succeeded" : "failed";
        string statusFormatted = allSuccess ? FormatSuccess(statusText, useColors) : FormatFailed(statusText, useColors);

        stdout.WriteLine();
        stdout.WriteLine($"workspace build {statusFormatted} in {FormatElapsed(workspaceTimer.Elapsed)}");
        stdout.WriteLine();
        stdout.WriteLine($"succeeded: {succeededCount}, skipped: {skippedCount}, failed: {failedCount}");
        stdout.WriteLine($"errors: {totalErrors}, warnings: {totalWarnings}");
        stdout.Flush();

        return allSuccess ? 0 : 1;
    }

    private sealed class ProjectCompilationResult
    {
        public bool Success { get; init; }
        public TimeSpan CompileElapsed { get; init; }
        public int ExitCode { get; init; }
        public int ErrorCount { get; init; }
        public int WarningCount { get; init; }
        public string Output { get; init; } = "";
        public string Diagnostics { get; init; } = "";
        public bool LaterStagesNotImplemented { get; init; }
    }

    private static ProjectCompilationResult CompileProject(
        string mahocPath,
        MahoProject project,
        string outputDir,
        bool checkOnly,
        bool stopAtIl,
        bool useColors,
        string? serverPath,
        string? compilerPackagePath)
    {
        var mahocArgs = new List<string>
        {
            "--color",
            useColors ? "always" : "never"
        };

        if (checkOnly)
            mahocArgs.Add("--check");
        else if (stopAtIl)
            mahocArgs.Add("--emit-il");

        mahocArgs.Add("-o");
        mahocArgs.Add(outputDir);

        if (project.EntryFile is not null)
        {
            mahocArgs.Add("--entry");
            mahocArgs.Add(project.EntryFile);
        }

        mahocArgs.Add($"--implicit-toplevel={(project.ImplicitTopLevel ? "true" : "false")}");

        if (project.GlobalUnsafeEnabled)
            mahocArgs.Add("--unsafe");

        foreach (var (aliasName, target) in project.GlobalAliases)
        {
            mahocArgs.Add("--alias");
            mahocArgs.Add($"{aliasName}={target}");
        }

        foreach (string refSpec in project.ProjectsReferenced)
        {
            MiryoBuildSystem.ParseReferenceSpec(refSpec, out string? refAlias, out string referencedProject);
            string referencePath = Path.IsPathRooted(referencedProject)
                ? referencedProject
                : Path.GetFullPath(Path.Combine(project.ProjectDirectory, referencedProject));
            mahocArgs.Add("--reference-project");
            mahocArgs.Add(refAlias is not null ? $"{refAlias}={referencePath}" : referencePath);
        }

        mahocArgs.Add("--project-name");
        mahocArgs.Add(project.ProjectName);
        mahocArgs.Add("--module-name");
        mahocArgs.Add(project.ModuleName);
        mahocArgs.Add("--project-directory");
        mahocArgs.Add(project.ProjectDirectory);
        mahocArgs.Add("--source-paths-stdin");

        if (serverPath is not null)
        {
            mahocArgs.Add("--server-path");
            mahocArgs.Add(serverPath);
        }

        if (compilerPackagePath is not null)
        {
            mahocArgs.Add("--compiler-package");
            mahocArgs.Add(compilerPackagePath);
        }

        var psi = MiryoConfiguration.CreateProcessStartInfo(mahocPath, mahocArgs);
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        string resultPipeName = $"miryo-result-{Guid.NewGuid():N}";
        psi.ArgumentList.Add("--result-pipe");
        psi.ArgumentList.Add(resultPipeName);

        var compileTimer = Stopwatch.StartNew();

        using var resultPipe = new NamedPipeServerStream(resultPipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var process = Process.Start(psi);

        if (process is null)
        {
            compileTimer.Stop();
            return new ProjectCompilationResult
            {
                Success = false,
                CompileElapsed = compileTimer.Elapsed,
                ExitCode = 1,
                ErrorCount = 1,
                Diagnostics = "error: failed to launch 'mahoc' process."
            };
        }

        foreach (string sourcePath in project.SourceFiles)
            process.StandardInput.WriteLine(sourcePath);
        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var diagnosticsTask = process.StandardError.ReadToEndAsync();
        using var pipeTimeout = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));
        Task pipeConnection = resultPipe.WaitForConnectionAsync(pipeTimeout.Token);
        Task processExit = process.WaitForExitAsync();

        if (Task.WhenAny(pipeConnection, processExit).GetAwaiter().GetResult() == processExit)
        {
            string compilerDiagnostics = diagnosticsTask.GetAwaiter().GetResult();
            return new ProjectCompilationResult
            {
                Success = false,
                CompileElapsed = compileTimer.Elapsed,
                ExitCode = process.ExitCode != 0 ? process.ExitCode : 1,
                ErrorCount = 1,
                Diagnostics = $"error: mahoc exited before writing its result pipe (exit code {process.ExitCode}). {compilerDiagnostics}".TrimEnd()
            };
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
        int errorCount = hasErrors ? 1 : 0;
        int warningCount = 0;
        bool laterStagesNotImplemented = false;

        if (!string.IsNullOrWhiteSpace(resultJson))
        {
            using JsonDocument result = JsonDocument.Parse(resultJson);
            var root = result.RootElement;

            if (root.TryGetProperty("elapsedTicks", out JsonElement ticks))
                elapsedTicks = ticks.GetInt64();

            if (root.TryGetProperty("hasErrors", out JsonElement errors))
                hasErrors = errors.GetBoolean();

            if (root.TryGetProperty("errorCount", out JsonElement ec))
                errorCount = ec.GetInt32();
            else if (hasErrors)
                errorCount = Math.Max(1, errorCount);

            if (root.TryGetProperty("warningCount", out JsonElement wc))
                warningCount = wc.GetInt32();

            laterStagesNotImplemented = root.TryGetProperty("pipelineError", out JsonElement pipeline) &&
                pipeline.ValueKind == JsonValueKind.String &&
                pipeline.GetString()?.Contains("lowering and code-generation", StringComparison.OrdinalIgnoreCase) == true;
        }

        return new ProjectCompilationResult
        {
            Success = !hasErrors && process.ExitCode == 0,
            CompileElapsed = TimeSpan.FromTicks(elapsedTicks),
            ExitCode = process.ExitCode,
            ErrorCount = errorCount,
            WarningCount = warningCount,
            Output = compilerOutput,
            Diagnostics = diagnostics,
            LaterStagesNotImplemented = laterStagesNotImplemented
        };
    }

    private static int ExecuteCompile(MiryoInvocation invocation, IToolchainManager toolchain, TextWriter stdout, TextWriter stderr)
    {
        string? colorMode = invocation.ColorMode ?? ExtractColorMode(invocation.Arguments);
        bool useColors = ShouldUseColors(colorMode);
        string? mahocPath = toolchain.FindTool("mahoc", invocation.TargetVersion);

        if (mahocPath is null)
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: could not locate 'mahoc' compiler executable for version {invocation.TargetVersion}.");
            return 1;
        }

        var psi = MiryoConfiguration.CreateProcessStartInfo(mahocPath, invocation.Arguments);
        psi.RedirectStandardOutput = false;
        psi.RedirectStandardError = false;

        using var process = Process.Start(psi);

        if (process is null)
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: failed to start mahoc process.");
            return 1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    private static int ExecuteRun(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        MiryoInvocation? invocation = null,
        IToolchainManager? toolchain = null)
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

        string? colorMode = invocation?.ColorMode ?? ExtractColorMode(args);
        bool useColors = ShouldUseColors(colorMode);

        string? targetPath = null;
        string? projectArg = null;

        for (int i = 0; i < buildArgs.Length; i++)
        {
            if ((buildArgs[i] == "-p" || buildArgs[i] == "--project") && i + 1 < buildArgs.Length)
            {
                projectArg = buildArgs[i + 1];
                i++;
            }
            else if (buildArgs[i].StartsWith("--project=", StringComparison.OrdinalIgnoreCase))
            {
                projectArg = buildArgs[i][(buildArgs[i].IndexOf('=') + 1)..];
            }
            else if (!buildArgs[i].StartsWith('-') && targetPath is null)
            {
                targetPath = buildArgs[i];
            }
        }

        targetPath ??= Directory.GetCurrentDirectory();
        if (Directory.Exists(targetPath) || File.Exists(targetPath))
        {
            var wsOrProj = MiryoBuildSystem.FindWorkspaceOrProject(targetPath);
            if (wsOrProj is { IsWorkspace: true } wsInfo)
            {
                MiryoWorkspace ws;
                try
                {
                    ws = MiryoBuildSystem.LoadWorkspace(wsInfo.FilePath);
                }
                catch (Exception ex)
                {
                    stderr.WriteLine($"{FormatError("error", useColors)}: failed to load workspace '{wsInfo.FilePath}': {ex.Message}");
                    return 1;
                }

                if (string.IsNullOrWhiteSpace(projectArg))
                {
                    if (ws.Configuration.Default.Length == 1)
                    {
                        projectArg = ws.Configuration.Default[0];
                    }
                    else if (ws.Projects.Count == 1)
                    {
                        projectArg = ws.Projects[0].ProjectName;
                    }
                    else
                    {
                        stderr.WriteLine($"{FormatError("error", useColors)}: workspace '{ws.Name}' contains multiple projects. Specify which project to run using '-p <project_name>'.");
                        return 1;
                    }

                    buildArgs = [.. buildArgs, "-p", projectArg];
                }
            }
        }

        int buildExit = ExecuteBuild(buildArgs, stdout, stderr, checkOnly: false, invocation: invocation, toolchain: toolchain);

        if (buildExit != 0)
            return buildExit;

        stderr.WriteLine($"{FormatError("error", useColors)}: run {FormatFailed("failed", useColors)}: Native code execution pipeline not implemented yet.");

        return 1;
    }

    private static int ExecuteTest(MiryoInvocation invocation, IToolchainManager toolchain, TextWriter stdout, TextWriter stderr)
    {
        stdout.WriteLine($"Running tests with toolchain v{invocation.TargetVersion}...");
        return ExecuteBuild(invocation.Arguments, stdout, stderr, checkOnly: true, invocation: invocation, toolchain: toolchain);
    }

    private static int ExecuteLs(MiryoInvocation invocation, IToolchainManager toolchain, TextWriter stdout, TextWriter stderr)
    {
        string? colorMode = invocation.ColorMode ?? ExtractColorMode(invocation.Arguments);
        bool useColors = ShouldUseColors(colorMode);
        string? lsPath = toolchain.FindTool("maho-ls", invocation.TargetVersion);

        if (lsPath is null)
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: maho-ls (v{invocation.TargetVersion}) language server is not installed.");
            return 1;
        }

        var psi = MiryoConfiguration.CreateProcessStartInfo(lsPath, invocation.Arguments);
        psi.RedirectStandardOutput = false;
        psi.RedirectStandardError = false;

        using var process = Process.Start(psi);

        if (process is null)
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: failed to launch maho-ls.");
            return 1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    private static int ExecuteTool(MiryoInvocation invocation, IToolchainManager toolchain, TextWriter stdout, TextWriter stderr)
    {
        string? colorMode = invocation.ColorMode;
        string? sub = null;

        for (int i = 0; i < invocation.Arguments.Length; i++)
        {
            string arg = invocation.Arguments[i];

            if (arg == "--color" && i + 1 < invocation.Arguments.Length && !invocation.Arguments[i + 1].StartsWith('-'))
                colorMode = invocation.Arguments[++i].ToLowerInvariant();
            else if (arg == "--color")
                colorMode = "always";
            else if (arg.StartsWith("--color=", StringComparison.OrdinalIgnoreCase))
                colorMode = arg[(arg.IndexOf('=') + 1)..].ToLowerInvariant();
            else if (!arg.StartsWith('-') && sub is null)
                sub = arg.ToLowerInvariant();
        }

        bool useColors = ShouldUseColors(colorMode);
        sub ??= "list";

        switch (sub)
        {
            case "list":
                stdout.WriteLine($"Active toolchain: v{toolchain.ActiveVersion}");
                stdout.WriteLine("Installed versions:");

                foreach (string v in toolchain.InstalledVersions)
                {
                    string marker = v == toolchain.ActiveVersion ? " (active)" : "";
                    stdout.WriteLine($"  - v{v}{marker}");
                }

                return 0;

            case "shims" or "update-shims":
                toolchain.UpdateShims(invocation.TargetVersion);
                stdout.WriteLine($"Updated shims for v{invocation.TargetVersion}.");

                return 0;

            default:
                stderr.WriteLine($"{FormatError("error", useColors)}: unknown tool command '{sub}'. Expected 'list' or 'update-shims'.");
                return 1;
        }
    }

    private static int ExecuteUpdate(MiryoInvocation invocation, IToolchainManager toolchain, TextWriter stdout, TextWriter stderr)
    {
        string currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "miryo");
        string binDir = Path.GetDirectoryName(Path.GetFullPath(currentExe))!;
        string updaterName = OperatingSystem.IsWindows() ? "miryo-update.exe" : "miryo-update";
        string updaterPath = Path.Combine(binDir, updaterName);

        if (!File.Exists(updaterPath))
            updaterPath = Path.Combine(AppContext.BaseDirectory, updaterName);

        string? stagedPath = null;
        string? colorMode = invocation.ColorMode;

        for (int i = 0; i < invocation.Arguments.Length; i++)
        {
            string arg = invocation.Arguments[i];

            if (arg == "--source" && i + 1 < invocation.Arguments.Length)
                stagedPath = invocation.Arguments[++i];
            else if (arg == "--color" && i + 1 < invocation.Arguments.Length && !invocation.Arguments[i + 1].StartsWith('-'))
                colorMode = invocation.Arguments[++i].ToLowerInvariant();
            else if (arg == "--color")
                colorMode = "always";
            else if (arg.StartsWith("--color=", StringComparison.OrdinalIgnoreCase))
                colorMode = arg[(arg.IndexOf('=') + 1)..].ToLowerInvariant();
        }

        bool useColors = ShouldUseColors(colorMode);

        if (stagedPath is null || !File.Exists(stagedPath) || !File.Exists(updaterPath))
        {
            stdout.WriteLine($"miryo is up to date (version {VersionString}).");
            return 0;
        }

        stdout.WriteLine("Applying miryo update...");
        using var pipeServer = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        string clientHandle = pipeServer.GetClientHandleAsString();

        var psi = new ProcessStartInfo(updaterPath)
        {
            UseShellExecute = false
        };
        psi.ArgumentList.Add("--pipe-handle");
        psi.ArgumentList.Add(clientHandle);
        psi.ArgumentList.Add("--target");
        psi.ArgumentList.Add(currentExe);
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(stagedPath);
        psi.ArgumentList.Add("--version");
        psi.ArgumentList.Add(invocation.TargetVersion);
        psi.ArgumentList.Add("--color");
        psi.ArgumentList.Add(useColors ? "always" : "never");

        using var updateProc = Process.Start(psi);
        if (updateProc is null)
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: failed to start miryo-update process.");
            return 1;
        }

        pipeServer.DisposeLocalCopyOfClientHandle();
        stdout.WriteLine("Spawning miryo-update and exiting to apply update.");

        return 0;
    }

    private static int ExecuteCompilerServer(string[] args, TextWriter stdout, TextWriter stderr, MiryoInvocation? invocation = null)
    {
        string? colorMode = invocation?.ColorMode;
        string? sub = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--color" && i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                colorMode = args[++i].ToLowerInvariant();
            else if (arg == "--color")
                colorMode = "always";
            else if (arg.StartsWith("--color=", StringComparison.OrdinalIgnoreCase))
                colorMode = arg[(arg.IndexOf('=') + 1)..].ToLowerInvariant();
            else if (!arg.StartsWith('-') && sub is null)
                sub = arg.ToLowerInvariant();
        }

        bool useColors = ShouldUseColors(colorMode);

        if (sub != "shutdown")
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: invalid arguments for compiler-server. Usage: miryo compiler-server shutdown [--color <mode>]");
            return 1;
        }

        string? serverManager = MahoPaths.FindServerManager();
        if (serverManager is null)
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: could not locate 'server-manager' executable in server directory.");
            return 1;
        }

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (serverManager.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add(serverManager);
        }
        else
        {
            startInfo.FileName = serverManager;
        }

        startInfo.ArgumentList.Add("shutdown");
        if (!string.IsNullOrEmpty(colorMode))
        {
            startInfo.ArgumentList.Add("--color");
            startInfo.ArgumentList.Add(colorMode);
        }

        using Process? process = Process.Start(startInfo);

        if (process is null)
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: failed to execute server-manager.");
            return 1;
        }

        string outText = process.StandardOutput.ReadToEnd();
        string errText = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(outText))
            stdout.Write(outText);
        if (!string.IsNullOrWhiteSpace(errText))
            stderr.Write(errText);

        return process.ExitCode;
    }

    private static int ExecuteClean(string[] args, TextWriter stdout, TextWriter stderr, MiryoInvocation? invocation = null)
    {
        string? targetDir = null;
        string? colorMode = invocation?.ColorMode;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg)
            {
                case "--color":
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        colorMode = args[++i].ToLowerInvariant();
                    else
                        colorMode = "always";
                    break;
                case string c when c.StartsWith("--color=", StringComparison.OrdinalIgnoreCase):
                    colorMode = c[(c.IndexOf('=') + 1)..].ToLowerInvariant();
                    break;
                default:
                    if (!arg.StartsWith('-') && targetDir is null)
                        targetDir = Path.GetFullPath(arg);

                    break;
            }
        }

        bool useColors = ShouldUseColors(colorMode);
        targetDir ??= Directory.GetCurrentDirectory();

        if (!Directory.Exists(targetDir) && !File.Exists(targetDir))
        {
            stderr.WriteLine($"{FormatError("error", useColors)}: directory not found: {targetDir}");
            return 1;
        }

        var wsOrProj = MiryoBuildSystem.FindWorkspaceOrProject(targetDir);
        if (wsOrProj is { IsWorkspace: true } wsInfo)
        {
            try
            {
                var ws = MiryoBuildSystem.LoadWorkspace(wsInfo.FilePath);
                int wsCleanedCount = 0;
                var cleanedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                void TryClean(string dir)
                {
                    if (Directory.Exists(dir) && cleanedDirs.Add(dir))
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                            wsCleanedCount++;
                        }
                        catch (Exception ex)
                        {
                            stderr.WriteLine($"{FormatError("error", useColors)}: failed to delete '{dir}': {ex.Message}");
                        }
                    }
                }

                TryClean(ws.TargetDirectory);

                foreach (var proj in ws.Projects)
                {
                    TryClean(Path.Combine(proj.ProjectDirectory, "target"));
                    TryClean(Path.Combine(proj.ProjectDirectory, "bin"));
                    TryClean(Path.Combine(proj.ProjectDirectory, "obj"));
                }

                stdout.WriteLine($"cleaned {wsCleanedCount} build artifact directories.");
                return 0;
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"{FormatError("error", useColors)}: failed to clean workspace: {ex.Message}");
                return 1;
            }
        }

        string cleanBaseDir = Directory.Exists(targetDir) ? targetDir : (Path.GetDirectoryName(targetDir) ?? targetDir);
        string[] cleanTargets = ["target", "bin", "obj"];
        int cleanedCount = 0;

        foreach (string target in cleanTargets)
        {
            string dir = Path.Combine(cleanBaseDir, target);

            if (Directory.Exists(dir))
            {
                try
                {
                    Directory.Delete(dir, true);
                    cleanedCount++;
                }
                catch (Exception ex)
                {
                    stderr.WriteLine($"{FormatError("error", useColors)}: failed to delete '{dir}': {ex.Message}");
                }
            }
        }

        stdout.WriteLine($"cleaned {cleanedCount} build artifact directories.");
        return 0;
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine(VersionString);
        writer.WriteLine("The project system and toolchain manager for the Maho programming language.");
        writer.WriteLine();
        writer.WriteLine("Usage: miryo [props] <command> [options]");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        writer.WriteLine("  new <name>       Create a new Maho project scaffolding (--bin or --lib)");
        writer.WriteLine("  build [path]     Compile the project, workspace, or directory");
        writer.WriteLine("  compile [flags]  Directly invoke the compiler (mahoc)");
        writer.WriteLine("  run [path]       Build and execute the program");
        writer.WriteLine("  test [path]      Run test suite for project or directory");
        writer.WriteLine("  ls [options]     Send request to language server (maho-ls)");
        writer.WriteLine("  tool [sub]       Manage toolchain tools and shims (list, update-shims)");
        writer.WriteLine("  update [options] Self-update miryo toolchain manager");
        writer.WriteLine("  clean [path]     Remove build artifact directories (target/, bin/, obj/)");
        writer.WriteLine("  compiler-server shutdown  Stop all compiler daemons owned by this user");
        writer.WriteLine();
        writer.WriteLine("Properties:");
        writer.WriteLine("  [v: <version>]   Specify target major.minor toolchain version (e.g. [v: 1.0])");
        writer.WriteLine();
        writer.WriteLine("General Options:");
        writer.WriteLine("  --config <path>  Specify toolchain configuration file (default: miryo.config)");
        writer.WriteLine("  --color <mode>   Control ANSI colored output (auto, always, never)");
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
        writer.WriteLine("  --color <mode>   Control ANSI colored output (auto, always, never)");
        writer.WriteLine("  -h, --help       Show this help text");
    }

    private static void PrintBuildUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: miryo build [path] [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  -p, --project <name>  Build only the specified project and its dependencies");
        writer.WriteLine("  --all                 Build all projects in the workspace");
        writer.WriteLine("  --exclude <name>      Exclude specific projects from being built");
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
    private const string AnsiYellow = "\u001b[33;1m";

    private static bool ShouldUseColors(string? colorMode) => colorMode switch
    {
        "never" or "false" or "off" => false,
        "always" or "true" or "on" => true,
        "auto" => (!Console.IsOutputRedirected || !Console.IsErrorRedirected) &&
                  Environment.GetEnvironmentVariable("NO_COLOR") == null,
        _ => Environment.GetEnvironmentVariable("NO_COLOR") == null
    };

    private static string FormatSuccess(string text, bool useColors) => useColors ? $"{AnsiGreen}{text}{AnsiReset}" : text;
    private static string FormatFailed(string text, bool useColors) => useColors ? $"{AnsiRed}{text}{AnsiReset}" : text;
    private static string FormatError(string text, bool useColors) => useColors ? $"{AnsiRed}{text}{AnsiReset}" : text;

    private static string FormatStatus(bool success, bool useColors)
    {
        string text = success ? "succeeded" : "failed";
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
            ? FormatSuccess("succeeded", useColors)
            : $"{FormatFailed("failed", useColors)}   ";
        writer.WriteLine($"{status} | {FormatElapsed(elapsed)}");
        writer.Flush();
    }
}
