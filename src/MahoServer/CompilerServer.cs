using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Maho.Server;

/// <summary>Wire request submitted by mahoc and other tools to the per-project compiler server.</summary>
public sealed record CompilerServerRequest(
    string ProjectName,
    string ModuleName,
    string RootDirectory,
    string[] SourcePaths,
    CompilationOptions Options,
    AnalysisOutput DebugOutput,
    bool CheckOnly);

public sealed record CompilerServerFileResult(
    string SourcePath,
    string? LexerJson,
    string? ParserJson,
    IReadOnlyList<DiagnosticInfo> Diagnostics,
    string? AnalysisError,
    bool IsInternalError);

/// <summary>Small, serialization-only result contract for build tools and the CLI wrapper.</summary>
public sealed record CompilerServerResponse(
    string ProjectName,
    CompilerServerFileResult[] Files,
    string? EntryFile,
    long ElapsedTicks,
    bool HasErrors,
    bool LaterStagesNotImplemented);

public static class CompilerServerHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static int Run(string[] args)
    {
        if (!TryParseArguments(args, out var pipeName, out var timeout, out bool oneShot))
            return 1;

        try
        {
            while (true)
            {
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                using var cancellation = timeout is null ? new CancellationTokenSource() : new CancellationTokenSource(timeout.Value);
                try
                {
                    pipe.WaitForConnectionAsync(cancellation.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                string? line = reader.ReadLine();
                if (line is null)
                    continue;

                CompilerServerResponse response;
                try
                {
                    var request = JsonSerializer.Deserialize<CompilerServerRequest>(line, JsonOptions)
                        ?? throw new InvalidDataException("Compiler request was empty.");
                    response = Compile(request);
                }
                catch (Exception ex)
                {
                    response = new CompilerServerResponse("<unknown>", [], null, 0, true, false);
                    Console.Error.WriteLine($"mahoc-server: {ex.Message}");
                }

                writer.WriteLine(JsonSerializer.Serialize(response, JsonOptions));

                if (oneShot)
                    break;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"mahoc-server: {ex.Message}");
            return 1;
        }
    }

    public static CompilerServerResponse Compile(CompilerServerRequest request)
    {
        var analysis = MahoCompiler.AnalyzeFiles(request.SourcePaths, request.DebugOutput, request.RootDirectory, request.Options);
        var files = analysis.Files.Select(file => new CompilerServerFileResult(
            file.SourcePath,
            file.DebugOutput?.LexerJson,
            file.DebugOutput?.ParserJson,
            file.Output?.Diagnostics ?? [],
            file.AnalysisError,
            file.IsInternalError)).ToArray();

        return new CompilerServerResponse(
            request.ProjectName,
            files,
            analysis.EntryFile,
            analysis.Elapsed.Ticks,
            analysis.HasErrors,
            !request.CheckOnly && !analysis.HasErrors);
    }

    private static bool TryParseArguments(string[] args, out string pipeName, out TimeSpan? timeout, out bool oneShot)
    {
        pipeName = string.Empty;
        timeout = TimeSpan.FromMinutes(5);
        oneShot = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pipe" when i + 1 < args.Length:
                    pipeName = args[++i];
                    break;
                case "--idle-timeout" when i + 1 < args.Length && int.TryParse(args[++i], out int seconds) && seconds >= 0:
                    oneShot = seconds == 0;
                    timeout = oneShot ? null : TimeSpan.FromSeconds(seconds);
                    break;
                case "--no-timeout":
                    timeout = null;
                    oneShot = false;
                    break;
                default:
                    Console.Error.WriteLine("Usage: mahoc-server --pipe <name> [--idle-timeout <seconds>|--no-timeout]");
                    return false;
            }
        }
        return !string.IsNullOrWhiteSpace(pipeName);
    }
}

/// <summary>Client transport and runner used by mahoc and toolchains.</summary>
public static class CompilerServerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CompilerServerResponse Compile(string projectDirectory, CompilerServerRequest request, string? serverExecutablePath = null)
    {
        if (string.IsNullOrWhiteSpace(serverExecutablePath))
            return CompilerServerHost.Compile(request);

        string resolvedServerExecutable = Path.GetFullPath(serverExecutablePath);
        if (!File.Exists(resolvedServerExecutable))
            throw new FileNotFoundException($"The specified compiler server executable was not found at '{serverExecutablePath}'.", serverExecutablePath);

        string pipeName = CreatePipeName(projectDirectory);
        if (!TrySend(pipeName, request, 150, out var response))
        {
            StartServer(pipeName, resolvedServerExecutable);
            if (!TrySend(pipeName, request, 5000, out response))
                throw new IOException("Could not connect to the compiler server after it was started.");
        }

        return response!;
    }

    public static string CreatePipeName(string projectDirectory)
    {
        byte[] bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(projectDirectory)));
        return "mahoc-" + Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static bool TrySend(string pipeName, CompilerServerRequest request, int timeoutMs, out CompilerServerResponse? response)
    {
        response = null;
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(timeoutMs);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            writer.WriteLine(JsonSerializer.Serialize(request, JsonOptions));
            response = JsonSerializer.Deserialize<CompilerServerResponse>(reader.ReadLine() ?? string.Empty, JsonOptions);
            return response is not null;
        }
        catch (IOException) { return false; }
        catch (SocketException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (TimeoutException) { return false; }
    }

    private static void StartServer(string pipeName, string serverExecutable)
    {
        var startInfo = new ProcessStartInfo(serverExecutable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        _ = Process.Start(startInfo) ?? throw new IOException("Failed to launch mahoc-server.");
    }
}
