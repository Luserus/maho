using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace Maho.Server;

/// <summary>
/// Hosts sessions for one user and one compiler package. The compiler itself
/// runs in a child process and communicates through a JSON protocol, so this
/// assembly never references a particular compiler implementation.
/// </summary>
public static class CompilerServerHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static int Run(string[] args)
    {
        if (!TryParseArguments(args, out string suppliedPipeName, out string packageDirectory, out TimeSpan? idleTimeout, out bool oneShot))
            return 1;

        CompilerPackageManifest manifest;
        CompilerIdentity compiler;
        try
        {
            manifest = CompilerPackageManifest.Load(packageDirectory);
            string compilerAssembly = Path.Combine(packageDirectory, manifest.CompilerAssembly);
            compiler = CompilerIdentity.FromCompilerAssembly(compilerAssembly);
            if (!string.Equals(manifest.CompilerVersion, compiler.Version, StringComparison.Ordinal))
                throw new InvalidDataException($"The manifest declares compiler version {manifest.CompilerVersion}, but {manifest.CompilerAssembly} is version {compiler.Version}.");
            if (manifest.CompilerHostProtocolVersion != CompilerHostProtocol.Version)
                throw new InvalidDataException($"This mahoc-server supports compiler-host protocol {CompilerHostProtocol.Version}, but the package requires {manifest.CompilerHostProtocolVersion}.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"mahoc-server: {ex.Message}");
            return 1;
        }

        string expectedPipeName = CompilerServerEndpoint.CreatePipeName(compiler);
        if (!string.Equals(suppliedPipeName, expectedPipeName, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("mahoc-server: the pipe does not belong to this compiler build.");
            return 1;
        }

        var state = new ServerState();
        var connections = new List<Task>();
        object compilerHostGate = new();

        try
        {
            using var compilerHost = new CompilerHostProcess(packageDirectory, manifest, compiler);
            using var shutdown = state.Shutdown;
            CompilerServerRegistry.Register(new CompilerServerRegistration(expectedPipeName, compiler.Version, compiler.Fingerprint));

            try
            {
                while (true)
                {
                    connections.RemoveAll(connection => connection.IsCompleted);
                    CancellationTokenSource cancellation;
                    lock (state.Gate)
                    {
                        PruneExpiredSessions(state.Sessions, idleTimeout);
                        if (state.ServedRequest && state.Sessions.Count == 0 && connections.Count == 0)
                            return 0;
                        cancellation = CreateAcceptCancellation(state.Sessions, idleTimeout, state.ServedRequest);
                    }

                    var pipe = new NamedPipeServerStream(expectedPipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    try
                    {
                        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, shutdown.Token);
                        pipe.WaitForConnectionAsync(linkedCancellation.Token).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                    {
                        pipe.Dispose();
                        return 0;
                    }
                    catch (OperationCanceledException)
                    {
                        pipe.Dispose();
                        continue;
                    }
                    finally
                    {
                        cancellation.Dispose();
                    }

                    Task connection = Task.Run(() => HandleConnection(pipe, compilerHost, compiler, state, compilerHostGate));
                    connections.Add(connection);
                    if (oneShot)
                    {
                        connection.GetAwaiter().GetResult();
                        return 0;
                    }
                }
            }
            finally
            {
                CompilerServerRegistry.Unregister(expectedPipeName);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"mahoc-server: {ex.Message}");
            return 1;
        }
    }

    private static void HandleConnection(NamedPipeServerStream pipe, CompilerHostProcess compilerHost, CompilerIdentity compiler, ServerState state, object compilerHostGate)
    {
        using (pipe)
        using (var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true))
        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
        {
            object response;
            try
            {
                string line = reader.ReadLine() ?? throw new InvalidDataException("Compiler request was empty.");
                using JsonDocument document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("method", out _))
                {
                    var control = JsonSerializer.Deserialize<CompilerServerControlRequest>(line, JsonOptions)
                        ?? throw new InvalidDataException("Compiler control request was empty.");
                    if (control.ProtocolVersion != CompilerProtocol.Version || !string.Equals(control.Method, "shutdown", StringComparison.Ordinal))
                        throw new InvalidDataException("Unsupported compiler server control request.");

                    response = new CompilerServerControlResponse(true, "Compiler server shutdown requested.");
                    writer.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
                    state.Shutdown.Cancel();
                    return;
                }

                var request = JsonSerializer.Deserialize<CompilerServerRequest>(line, JsonOptions)
                    ?? throw new InvalidDataException("Compiler request was empty.");
                if (request.ProtocolVersion != CompilerProtocol.Version)
                    throw new InvalidDataException("Compiler request uses an unsupported protocol version.");

                CompilerSession session;
                lock (state.Gate)
                {
                    session = GetOrCreateSession(state.Sessions, CreateSessionKey(request), request);
                    session.ActiveRequests++;
                }

                try
                {
                    CompilerServerResponse compileResponse;
                    lock (compilerHostGate)
                        compileResponse = compilerHost.Compile(request);
                    if (!string.Equals(compileResponse.CompilerVersion, compiler.Version, StringComparison.Ordinal) ||
                        !string.Equals(compileResponse.CompilerFingerprint, compiler.Fingerprint, StringComparison.Ordinal))
                        throw new InvalidDataException("The compiler host reported a different compiler package.");
                    response = compileResponse;

                    lock (state.Gate)
                    {
                        session.LastAccessUtc = DateTimeOffset.UtcNow;
                        state.ServedRequest = true;
                    }
                }
                finally
                {
                    lock (state.Gate)
                        session.ActiveRequests--;
                }
            }
            catch (Exception ex)
            {
                response = new CompilerServerResponse("<unknown>", [], null, 0, true, false, compiler.Version, compiler.Fingerprint);
                Console.Error.WriteLine($"mahoc-server: {ex.Message}");
            }

            writer.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
        }
    }

    private static CompilerSession GetOrCreateSession(Dictionary<string, CompilerSession> sessions, string key, CompilerServerRequest request)
    {
        if (!sessions.TryGetValue(key, out var session))
        {
            session = new CompilerSession(key, request.ProjectName, request.RootDirectory, request.ModuleName, DateTimeOffset.UtcNow);
            sessions.Add(key, session);
        }
        return session;
    }

    private static string CreateSessionKey(CompilerServerRequest request) => string.Join("|", Path.GetFullPath(request.RootDirectory), request.ModuleName, request.Options.EntryFile ?? string.Empty);

    private static void PruneExpiredSessions(Dictionary<string, CompilerSession> sessions, TimeSpan? timeout)
    {
        if (timeout is null)
            return;

        DateTimeOffset cutoff = DateTimeOffset.UtcNow - timeout.Value;
        foreach (string key in sessions.Where(pair => pair.Value.ActiveRequests == 0 && pair.Value.LastAccessUtc <= cutoff).Select(pair => pair.Key).ToArray())
            sessions.Remove(key);
    }

    private static CancellationTokenSource CreateAcceptCancellation(Dictionary<string, CompilerSession> sessions, TimeSpan? timeout, bool servedRequest)
    {
        if (timeout is null)
            return new CancellationTokenSource();

        TimeSpan delay = servedRequest && sessions.Count > 0
            ? sessions.Values.Where(session => session.ActiveRequests == 0).Select(session => (session.LastAccessUtc + timeout.Value) - DateTimeOffset.UtcNow).DefaultIfEmpty(timeout.Value).Min()
            : timeout.Value;
        return new CancellationTokenSource(delay > TimeSpan.Zero ? delay : TimeSpan.Zero);
    }

    private static bool TryParseArguments(string[] args, out string pipeName, out string packageDirectory, out TimeSpan? timeout, out bool oneShot)
    {
        pipeName = string.Empty;
        packageDirectory = string.Empty;
        timeout = TimeSpan.FromMinutes(5);
        oneShot = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pipe" when i + 1 < args.Length: pipeName = args[++i]; break;
                case "--compiler-package" when i + 1 < args.Length: packageDirectory = Path.GetFullPath(args[++i]); break;
                case "--idle-timeout" when i + 1 < args.Length && int.TryParse(args[++i], out int seconds) && seconds >= 0:
                    oneShot = seconds == 0;
                    timeout = oneShot ? null : TimeSpan.FromSeconds(seconds);
                    break;
                case "--no-timeout": timeout = null; oneShot = false; break;
                default:
                    Console.Error.WriteLine("Usage: mahoc-server --pipe <name> --compiler-package <directory> [--idle-timeout <seconds>|--no-timeout]");
                    return false;
            }
        }
        return !string.IsNullOrWhiteSpace(pipeName) && !string.IsNullOrWhiteSpace(packageDirectory);
    }

    private sealed class CompilerSession(string key, string projectName, string rootDirectory, string moduleName, DateTimeOffset lastAccessUtc)
    {
        public string Key { get; } = key;
        public string ProjectName { get; } = projectName;
        public string RootDirectory { get; } = rootDirectory;
        public string ModuleName { get; } = moduleName;
        public DateTimeOffset LastAccessUtc { get; set; } = lastAccessUtc;
        public int ActiveRequests { get; set; }
    }

    private sealed class ServerState
    {
        public object Gate { get; } = new();
        public Dictionary<string, CompilerSession> Sessions { get; } = new(StringComparer.Ordinal);
        public CancellationTokenSource Shutdown { get; } = new();
        public bool ServedRequest { get; set; }
    }

    private sealed class CompilerHostProcess : IDisposable
    {
        private readonly string packageDirectory;
        private readonly CompilerPackageManifest manifest;
        private readonly CompilerIdentity compiler;
        private Process? process;
        private StreamWriter? writer;
        private StreamReader? reader;

        public CompilerHostProcess(string packageDirectory, CompilerPackageManifest manifest, CompilerIdentity compiler)
        {
            this.packageDirectory = packageDirectory;
            this.manifest = manifest;
            this.compiler = compiler;
        }

        public CompilerServerResponse Compile(CompilerServerRequest request)
        {
            EnsureStarted();
            string id = Guid.NewGuid().ToString("N");
            Write(new { id, method = "compile", @params = request });
            using JsonDocument response = Read(id);

            if (!response.RootElement.TryGetProperty("result", out JsonElement result))
                throw new InvalidDataException("The compiler host returned no compilation result.");
            return JsonSerializer.Deserialize<CompilerServerResponse>(result.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("The compiler host returned an invalid compilation result.");
        }

        private void EnsureStarted()
        {
            if (process is not null && !process.HasExited)
                return;

            string hostCommand = manifest.HostCommand;
            if (hostCommand.StartsWith("./", StringComparison.Ordinal) || hostCommand.StartsWith(".\\", StringComparison.Ordinal))
                hostCommand = Path.GetFullPath(Path.Combine(packageDirectory, hostCommand[2..]));

            var startInfo = new ProcessStartInfo(hostCommand)
            {
                WorkingDirectory = packageDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in manifest.HostArguments)
                startInfo.ArgumentList.Add(argument);

            process = Process.Start(startInfo) ?? throw new IOException("Failed to launch the compiler host.");
            process.BeginErrorReadLine();
            writer = process.StandardInput;
            writer.AutoFlush = true;
            reader = process.StandardOutput;

            string id = Guid.NewGuid().ToString("N");
            Write(new { id, method = "hello", @params = new { protocolVersion = CompilerHostProtocol.Version } });
            using JsonDocument response = Read(id);
            JsonElement result = response.RootElement.GetProperty("result");
            int protocolVersion = result.GetProperty("compilerHostProtocolVersion").GetInt32();
            string version = result.GetProperty("compilerVersion").GetString() ?? string.Empty;
            string fingerprint = result.GetProperty("compilerFingerprint").GetString() ?? string.Empty;

            if (protocolVersion != CompilerHostProtocol.Version)
                throw new InvalidDataException($"The compiler host uses unsupported protocol {protocolVersion}.");
            if (!string.Equals(version, compiler.Version, StringComparison.Ordinal) || !string.Equals(fingerprint, compiler.Fingerprint, StringComparison.Ordinal))
                throw new InvalidDataException("The compiler host does not match the requested compiler package.");
        }

        private void Write(object message)
        {
            if (writer is null)
                throw new InvalidOperationException("The compiler host has not started.");
            writer.WriteLine(JsonSerializer.Serialize(message, JsonOptions));
        }

        private JsonDocument Read(string expectedId)
        {
            if (reader is null)
                throw new InvalidOperationException("The compiler host has not started.");
            string? line = reader.ReadLine();
            if (line is null)
                throw new IOException("The compiler host exited without returning a response.");

            JsonDocument response = JsonDocument.Parse(line);
            JsonElement root = response.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id) || !string.Equals(id.GetString(), expectedId, StringComparison.Ordinal))
            {
                response.Dispose();
                throw new InvalidDataException("The compiler host returned a response for a different request.");
            }
            if (!root.TryGetProperty("ok", out JsonElement ok) || !ok.GetBoolean())
            {
                string error = root.TryGetProperty("error", out JsonElement errorElement) ? errorElement.GetString() ?? "unknown host error" : "unknown host error";
                response.Dispose();
                throw new InvalidOperationException($"Compiler host failed: {error}");
            }
            return response;
        }

        public void Dispose()
        {
            writer?.Dispose();
            reader?.Dispose();
            if (process is { HasExited: false })
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            process?.Dispose();
        }
    }
}
