using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
namespace Maho.Server;

/// <summary>Describes a generic server and a separately deployable compiler package.</summary>
public sealed record CompilerServerPackage(string ServerExecutablePath, string PackageDirectory, CompilerPackageManifest Manifest, CompilerIdentity Compiler);

/// <summary>Shared daemon discovery and request transport for CLI and tooling clients.</summary>
public static class CompilerServerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CompilerServerPackage LoadPackage(string serverExecutablePath, string? compilerPackagePath = null)
    {
        string serverPath = Path.GetFullPath(serverExecutablePath);
        if (!File.Exists(serverPath))
            throw new FileNotFoundException("The compiler server executable was not found.", serverPath);

        string packageDirectory = ResolvePackageDirectory(serverPath, compilerPackagePath);
        CompilerPackageManifest manifest = CompilerPackageManifest.Load(packageDirectory);
        if (manifest.CompilerHostProtocolVersion != CompilerHostProtocol.Version)
            throw new InvalidDataException($"The compiler package requires host protocol {manifest.CompilerHostProtocolVersion}, but this tooling supports {CompilerHostProtocol.Version}.");

        string compilerAssembly = Path.Combine(packageDirectory, manifest.CompilerAssembly);
        if (!File.Exists(compilerAssembly))
            throw new FileNotFoundException("The compiler library declared by the package manifest was not found.", compilerAssembly);

        CompilerIdentity compiler = CompilerIdentity.FromCompilerAssembly(compilerAssembly);
        if (!string.Equals(manifest.CompilerVersion, compiler.Version, StringComparison.Ordinal))
            throw new InvalidDataException($"The compiler package manifest declares version {manifest.CompilerVersion}, but its compiler assembly is version {compiler.Version}.");

        return new CompilerServerPackage(serverPath, packageDirectory, manifest, compiler);
    }

    public static CompilerServerResponse Compile(CompilerServerPackage package, CompilerServerRequest request)
    {
        if (request.ProtocolVersion != CompilerProtocol.Version)
            throw new InvalidOperationException("The client used an unsupported compiler-server protocol version.");

        string pipeName = CompilerServerEndpoint.CreatePipeName(package.Compiler);
        if (!TrySend(pipeName, request, 150, out var response))
        {
            response = StartOrFindServer(pipeName, package, request);
        }

        if (response!.CompilerFingerprint != package.Compiler.Fingerprint)
            throw new InvalidOperationException("The connected compiler server does not match the requested compiler package.");

        return response;
    }

    /// <summary>Requests shutdown of every live daemon registered for this user.</summary>
    public static int ShutdownAllServers()
    {
        int stopped = 0;
        foreach (CompilerServerRegistration registration in CompilerServerRegistry.GetRegistrations())
        {
            if (TryShutdown(registration.PipeName))
                stopped++;
            // A daemon removes its own entry while exiting. Removing here also
            // clears stale registrations left by a terminated process.
            CompilerServerRegistry.Unregister(registration.PipeName);
        }
        return stopped;
    }

    private static CompilerServerResponse StartOrFindServer(string pipeName, CompilerServerPackage package, CompilerServerRequest request)
    {
        using var launchGate = new Mutex(initiallyOwned: false, $"{pipeName}-launch", out bool createdNew);
        try
        {
            launchGate.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // The previous launcher crashed before it could release the gate.
            // Ownership has transferred to this client, which can safely retry.
        }

        try
        {
            // The first launcher starts immediately, avoiding a cold-start
            // probe. Concurrent clients instead wait briefly for it to finish.
            if (!createdNew && TrySend(pipeName, request, 500, out var existing))
                return existing!;

            StartServer(pipeName, package);
            if (!TrySend(pipeName, request, 5000, out var started))
                throw new IOException("Could not connect to the compiler server after it was started.");
            return started!;
        }
        finally
        {
            try { launchGate.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
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

    private static string ResolvePackageDirectory(string serverPath, string? suppliedPath)
    {
        if (!string.IsNullOrWhiteSpace(suppliedPath))
        {
            string fullPath = Path.GetFullPath(suppliedPath);
            if (File.Exists(fullPath) && string.Equals(Path.GetFileName(fullPath), CompilerPackageManifest.FileName, StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(fullPath)!;
            if (Directory.Exists(fullPath))
                return fullPath;
            throw new DirectoryNotFoundException($"The compiler package path does not exist: {suppliedPath}");
        }

        // This is only a convenience for a self-contained development bundle.
        // Deliberately do not walk parent directories or consult environment
        // variables: ordinary installs select a package explicitly.
        string colocated = Path.GetDirectoryName(serverPath)!;
        if (File.Exists(Path.Combine(colocated, CompilerPackageManifest.FileName)))
            return colocated;

        throw new FileNotFoundException("No compiler package was selected. Pass --compiler-package or configure mahoc_compiler in miryo.config.",
            Path.Combine(colocated, CompilerPackageManifest.FileName));
    }

    private static void StartServer(string pipeName, CompilerServerPackage package)
    {
        // A daemon must not inherit a test runner's captured stdout/stderr.
        // Such inherited handles keep the runner's output pipe open for the
        // daemon's five-minute idle lifetime, making the parent appear stuck.
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (package.ServerExecutablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add(package.ServerExecutablePath);
        }
        else
        {
            startInfo.FileName = package.ServerExecutablePath;
        }
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--compiler-package");
        startInfo.ArgumentList.Add(package.PackageDirectory);
        using Process process = Process.Start(startInfo) ?? throw new IOException("Failed to launch mahoc-server.");
    }
}
