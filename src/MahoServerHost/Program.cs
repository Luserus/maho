using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Maho.Server;

/// <summary>
/// Version-coupled compiler server daemon. Directly links Maho.dll in-process
/// at startup, avoiding secondary IPC hops or child adapter processes.
/// </summary>
public static class MahoServerDaemon
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string AnsiReset = "\u001b[0m";
    private const string AnsiRed = "\u001b[31;1m";

    private static bool ShouldUseColors(string? colorMode) => colorMode switch
    {
        "never" or "false" or "off" => false,
        "always" or "true" or "on" => true,
        "auto" => (!Console.IsOutputRedirected || !Console.IsErrorRedirected) &&
                  Environment.GetEnvironmentVariable("NO_COLOR") == null,
        _ => Environment.GetEnvironmentVariable("NO_COLOR") == null
    };

    private static string FormatError(string text, bool useColors) => useColors ? $"{AnsiRed}{text}{AnsiReset}" : text;

    public static int Main(string[] args)
    {
        if (!TryParseArguments(args, out string pipeName, out string version, out TimeSpan? idleTimeout, out bool oneShot, out bool useColors))
            return 1;

        var identity = CompilerIdentity.ForVersion(version);
        string normVersion = identity.Version;

        using var shutdown = new CancellationTokenSource();
        var connections = new List<Task>();
        DateTimeOffset lastActivityUtc = DateTimeOffset.UtcNow;
        bool servedRequest = false;

        CompilerServerRegistry.Register(new CompilerServerRegistration(pipeName, identity.Version, identity.Fingerprint));

        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                connections.RemoveAll(c => c.IsCompleted);

                // Check idle timeout
                if (idleTimeout is { } timeout && servedRequest && connections.Count == 0)
                {
                    if (DateTimeOffset.UtcNow - lastActivityUtc >= timeout)
                        return 0;
                }

                CancellationTokenSource acceptCts;
                if (idleTimeout is { } t && connections.Count == 0)
                {
                    TimeSpan remaining = servedRequest
                        ? (lastActivityUtc + t) - DateTimeOffset.UtcNow
                        : t;
                    acceptCts = new CancellationTokenSource(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
                }
                else
                {
                    acceptCts = new CancellationTokenSource();
                }

                var pipe = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                try
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(acceptCts.Token, shutdown.Token);
                    pipe.WaitForConnectionAsync(linked.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                {
                    pipe.Dispose();
                    return 0;
                }
                catch (OperationCanceledException)
                {
                    pipe.Dispose();
                    if (servedRequest && connections.Count == 0)
                        return 0;
                    continue;
                }
                finally
                {
                    acceptCts.Dispose();
                }

                Task connection = Task.Run(() =>
                {
                    HandleConnection(pipe, identity, shutdown, useColors);
                });

                connections.Add(connection);
                lastActivityUtc = DateTimeOffset.UtcNow;
                servedRequest = true;

                if (oneShot)
                {
                    connection.GetAwaiter().GetResult();
                    return 0;
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{FormatError("error", useColors)}: {ex.Message}");
            return 1;
        }
        finally
        {
            CompilerServerRegistry.Unregister(pipeName);
        }
    }

    private static void HandleConnection(NamedPipeServerStream pipe, CompilerIdentity identity, CancellationTokenSource shutdown, bool useColors)
    {
        using (pipe)
        using (var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true))
        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
        {
            try
            {
                string? line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                    return;

                using JsonDocument doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("method", out _))
                {
                    var control = JsonSerializer.Deserialize<CompilerServerControlRequest>(line, JsonOptions);
                    if (control is not null && string.Equals(control.Method, "shutdown", StringComparison.OrdinalIgnoreCase))
                    {
                        var controlResponse = new CompilerServerControlResponse(true, "Shutdown requested.");
                        writer.WriteLine(JsonSerializer.Serialize(controlResponse, JsonOptions));
                        shutdown.Cancel();
                        return;
                    }
                }

                var request = JsonSerializer.Deserialize<CompilerServerRequest>(line, JsonOptions);
                if (request is null)
                    return;

                var compilationOptions = new CompilationOptions
                {
                    ColorMode = (DiagnosticColorMode)request.Options.ColorMode,
                    PathStyle = (DiagnosticPathStyle)request.Options.PathStyle,
                    OutputFormat = (DiagnosticFormat)request.Options.OutputFormat,
                    WarningsAsErrors = request.Options.WarningsAsErrors,
                    EntryFile = request.Options.EntryFile,
                    ImplicitTopLevel = request.Options.ImplicitTopLevel,
                    RootDirectory = request.Options.RootDirectory,
                    ReferencedProjects = request.Options.ReferencedProjects,
                    GlobalAliases = request.Options.GlobalAliases,
                    ProjectFilePath = request.Options.ProjectFilePath,
                    MacroRecursionLimit = request.Options.MacroRecursionLimit
                };

                var compilation = MahoCompiler.AnalyzeFiles(
                    request.SourcePaths,
                    request.RootDirectory,
                    compilationOptions,
                    request.ProjectName);

                var filesList = compilation.SyntaxTrees.Select(tree => new CompilerServerFileResult(
                    tree.FilePath,
                    tree.Diagnostics.Select(DiagnosticWireMapper.ToWireDiagnostic).ToArray(),
                    null,
                    false)).ToList();

                foreach (var (path, (errorMsg, isInternal)) in compilation.FileErrors)
                {
                    filesList.Add(new CompilerServerFileResult(path, [], errorMsg, isInternal));
                }

                var response = new CompilerServerResponse(
                    request.ProjectName,
                    filesList.ToArray(),
                    compilation.EntryFile,
                    compilation.Elapsed.Ticks,
                    compilation.HasErrors,
                    !request.CheckOnly && !compilation.HasErrors,
                    identity.Version,
                    identity.Fingerprint);

                writer.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
            }
            catch (Exception ex)
            {
                var errResponse = new CompilerServerResponse(
                    "<error>",
                    [],
                    null,
                    0,
                    true,
                    false,
                    identity.Version,
                    identity.Fingerprint);

                writer.WriteLine(JsonSerializer.Serialize(errResponse, JsonOptions));
                Console.Error.WriteLine($"{FormatError("error", useColors)}: connection error: {ex.Message}");
            }
        }
    }

    private static bool TryParseArguments(string[] args, out string pipeName, out string version, out TimeSpan? idleTimeout, out bool oneShot, out bool useColors)
    {
        pipeName = string.Empty;
        version = "1.0";
        idleTimeout = TimeSpan.FromMinutes(5);
        oneShot = false;
        string? colorMode = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pipe" when i + 1 < args.Length:
                    pipeName = args[++i];
                    break;
                case "--version" when i + 1 < args.Length:
                    version = args[++i];
                    break;
                case "--idle-timeout" when i + 1 < args.Length && int.TryParse(args[++i], out int seconds) && seconds >= 0:
                    oneShot = seconds == 0;
                    idleTimeout = oneShot ? null : TimeSpan.FromSeconds(seconds);
                    break;
                case "--no-timeout":
                    idleTimeout = null;
                    oneShot = false;
                    break;
                case "--color" when i + 1 < args.Length && !args[i + 1].StartsWith('-'):
                    colorMode = args[++i].ToLowerInvariant();
                    break;
                case "--color":
                    colorMode = "always";
                    break;
                case string c when c.StartsWith("--color=", StringComparison.OrdinalIgnoreCase):
                    colorMode = c[(c.IndexOf('=') + 1)..].ToLowerInvariant();
                    break;
                default:
                    useColors = ShouldUseColors(colorMode);
                    Console.Error.WriteLine($"{FormatError("error", useColors)}: invalid arguments. Usage: maho-server --pipe <name> [--version <major.minor>] [--idle-timeout <seconds>|--no-timeout] [--color <mode>]");
                    return false;
            }
        }

        useColors = ShouldUseColors(colorMode);

        if (string.IsNullOrWhiteSpace(pipeName))
        {
            pipeName = CompilerServerEndpoint.CreatePipeName(version);
        }

        return true;
    }
}
