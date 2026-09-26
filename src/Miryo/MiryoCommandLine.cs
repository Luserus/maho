using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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

    public static int Run(string[] args) => Run(args, Console.Out, Console.Error);

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
                    stderr.WriteLine();
                    PrintUsage(stderr);
                    return 1;
                }

                // If path passed directly without command, treat as 'build <path>'
                return ExecuteBuild(args, stdout, stderr);
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

        if (checkOnly)
            mahocArgs.Add("--check");
        else if (stopAtIl)
            mahocArgs.Add("--emit-il");

        if (outputDir is not null)
        {
            mahocArgs.Add("-o");
            mahocArgs.Add(outputDir);
        }

        if (noProject)
        {
            if (File.Exists(fullTargetPath))
            {
                mahocArgs.Add(fullTargetPath);
            }
            else if (Directory.Exists(fullTargetPath))
            {
                mahocArgs.Add(fullTargetPath);
            }
            else
            {
                stderr.WriteLine($"Input path not found: {fullTargetPath}");
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
                    stderr.WriteLine($"No project file ('.mhpr') found in '{fullTargetPath}'. Use --no-project to compile without a project file.");
                    return 1;
                }
                projectFilePath = found;
            }
            else
            {
                stderr.WriteLine($"Target path not found: {fullTargetPath}");
                return 1;
            }

            MahoProject project;
            try
            {
                project = MiryoBuildSystem.LoadProject(projectFilePath);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"Failed to load project '{projectFilePath}': {ex.Message}");
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
                    stderr.WriteLine($"Referenced project not found: {refPath}");
                    return 1;
                }

                int refExit = ExecuteBuild([refPath], stdout, stderr, checkOnly);
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

            foreach (string file in project.SourceFiles)
                mahocArgs.Add(file);
        }

        try
        {
            var psi = MiryoConfiguration.CreateProcessStartInfo(mahocPath, mahocArgs);
            using var process = Process.Start(psi);
            if (process is null)
            {
                stderr.WriteLine("Failed to launch 'mahoc' process.");
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Error launching compiler: {ex.Message}");
            return 1;
        }
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

        stderr.WriteLine("Run failed: Native code execution pipeline not implemented yet.");
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
        writer.WriteLine("  --config <path>       Specify toolchain configuration file (default: miryo.config)");
        writer.WriteLine("  -h, --help            Show this help text");
    }
}
