using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Maho.Server;

/// <summary>
/// Client interface for interacting with versioned compiler server daemons.
/// Tools like mahoc, maho-ls, and miryo use this to delegate compilation to the appropriate daemon.
/// </summary>
public static class CompilerServerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Executes a compilation request using the compiler daemon for the specified version.
    /// Spawns the daemon process if it is not already running.
    /// </summary>
    public static CompilerServerResponse Compile(string version, CompilerServerRequest request, string? mahoHome = null)
    {
        if (request.ProtocolVersion != CompilerProtocol.Version)
            throw new InvalidOperationException("The client used an unsupported compiler-server protocol version.");

        string normVersion = CompilerServerEndpoint.NormalizeVersion(version);
        string pipeName = CompilerServerEndpoint.CreatePipeName(normVersion);

        if (!TrySend(pipeName, request, 150, out var response))
            response = StartOrFindServer(pipeName, normVersion, request, mahoHome);

        return response!;
    }

    /// <summary>
    /// Requests shutdown of every live daemon registered for this user.
    /// </summary>
    public static int ShutdownAllServers()
    {
        int stopped = 0;

        foreach (CompilerServerRegistration registration in CompilerServerRegistry.GetRegistrations())
        {
            if (TryShutdown(registration.PipeName))
                stopped++;

            CompilerServerRegistry.Unregister(registration.PipeName);
        }

        return stopped;
    }

    /// <summary>
    /// Requests shutdown of a specific compiler server daemon by version.
    /// </summary>
    public static bool ShutdownServer(string version)
    {
        string pipeName = CompilerServerEndpoint.CreatePipeName(version);
        bool success = TryShutdown(pipeName);
        CompilerServerRegistry.Unregister(pipeName);

        return success;
    }

    private static CompilerServerResponse StartOrFindServer(string pipeName, string version, CompilerServerRequest request, string? mahoHome)
    {
        using var launchGate = new Mutex(initiallyOwned: false, $"{pipeName}-launch", out bool createdNew);

        try
        {
            launchGate.WaitOne();
        }
        catch (AbandonedMutexException)
        {
        }

        try
        {
            if (!createdNew && TrySend(pipeName, request, 500, out var existing))
                return existing!;

            StartServer(pipeName, version, mahoHome);

            if (!TrySend(pipeName, request, 5000, out var started))
                throw new IOException($"Could not connect to the compiler server for version {version} after starting it.");

            return started!;
        }
        finally
        {
            try { launchGate.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
    }

    private static void StartServer(string pipeName, string version, string? mahoHome)
    {
        string serverExe = FindServerExecutable(version, mahoHome);

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (serverExe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            string home = ResolveMahoHome(mahoHome);
            string runtimeDotNet = Path.Combine(home, "runtime", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            startInfo.FileName = File.Exists(runtimeDotNet) ? runtimeDotNet : "dotnet";
            startInfo.ArgumentList.Add(serverExe);
        }
        else
            startInfo.FileName = serverExe;

        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--version");
        startInfo.ArgumentList.Add(version);

        using Process process = Process.Start(startInfo) ?? throw new IOException($"Failed to launch maho-server for version {version}.");
    }

    private static string ResolveMahoHome(string? mahoHome)
    {
        if (!string.IsNullOrWhiteSpace(mahoHome))
            return Path.GetFullPath(mahoHome);

        string? env = Environment.GetEnvironmentVariable("MAHO_HOME");

        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);

        string baseDir = AppContext.BaseDirectory;
        string parentDir = Path.GetDirectoryName(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? baseDir;
        string grandparent = Path.GetDirectoryName(parentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? parentDir;
        if (Directory.Exists(Path.Combine(grandparent, "tools")) &&
            (Directory.Exists(Path.Combine(grandparent, "server")) || Directory.Exists(Path.Combine(grandparent, "compiler")) || Directory.Exists(Path.Combine(grandparent, "runtime"))))
            return Path.GetFullPath(grandparent);

        if (Directory.Exists(Path.Combine(parentDir, "tools")) &&
            (Directory.Exists(Path.Combine(parentDir, "server")) || Directory.Exists(Path.Combine(parentDir, "compiler")) || Directory.Exists(Path.Combine(parentDir, "runtime"))))
            return Path.GetFullPath(parentDir);

        if (Directory.Exists(Path.Combine(baseDir, "tools")))
            return Path.GetFullPath(baseDir);

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".maho");
    }

    private static string FindServerExecutable(string version, string? mahoHome)
    {
        string normVersion = CompilerServerEndpoint.NormalizeVersion(version);
        string home = ResolveMahoHome(mahoHome);
        string toolDir = Path.Combine(home, "tools", normVersion);
        string exeName = OperatingSystem.IsWindows() ? "maho-server.exe" : "maho-server";

        string toolPath = Path.Combine(toolDir, exeName);

        if (File.Exists(toolPath))
            return toolPath;

        string dllPath = Path.Combine(toolDir, "maho-server.dll");

        if (File.Exists(dllPath))
            return dllPath;

        // Fallback for development / co-located build output
        string baseDir = AppContext.BaseDirectory;
        string colocatedExe = Path.Combine(baseDir, exeName);

        if (File.Exists(colocatedExe))
            return colocatedExe;

        string colocatedDll = Path.Combine(baseDir, "maho-server.dll");

        if (File.Exists(colocatedDll))
            return colocatedDll;

        // Also check mahoc-server names during migration
        string oldExe = OperatingSystem.IsWindows() ? "mahoc-server.exe" : "mahoc-server";

        if (File.Exists(Path.Combine(baseDir, oldExe)))
            return Path.Combine(baseDir, oldExe);

        throw new FileNotFoundException($"Compiler server executable '{exeName}' was not found for version {version} in '{toolDir}'.", toolPath);
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
            string? line = reader.ReadLine();

            if (string.IsNullOrWhiteSpace(line))
                return false;

            response = JsonSerializer.Deserialize<CompilerServerResponse>(line, JsonOptions);

            return response is not null;
        }
        catch (IOException) { return false; }
        catch (SocketException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (TimeoutException) { return false; }
    }

    private static bool TryShutdown(string pipeName)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(500);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            writer.WriteLine(JsonSerializer.Serialize(new CompilerServerControlRequest(CompilerProtocol.Version, "shutdown"), JsonOptions));
            var response = JsonSerializer.Deserialize<CompilerServerControlResponse>(reader.ReadLine() ?? string.Empty, JsonOptions);

            return response?.Success == true;
        }
        catch (IOException) { return false; }
        catch (SocketException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (TimeoutException) { return false; }
    }
}
