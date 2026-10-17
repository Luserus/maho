using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Maho.Server.Protocol;

namespace Maho.Server.Client;

/// <summary>Describes a colocated compiler-server package without loading its compiler assembly.</summary>
public sealed record CompilerServerPackage(string ServerExecutablePath, string CompilerAssemblyPath, CompilerIdentity Compiler);

/// <summary>Shared daemon discovery and request transport for CLI and tooling clients.</summary>
public static class CompilerServerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CompilerServerPackage LoadPackage(string serverExecutablePath)
    {
        string serverPath = Path.GetFullPath(serverExecutablePath);
        if (!File.Exists(serverPath))
            throw new FileNotFoundException("The compiler server executable was not found.", serverPath);

        string compilerAssembly = Path.Combine(Path.GetDirectoryName(serverPath)!, "Maho.dll");
        if (!File.Exists(compilerAssembly))
            throw new FileNotFoundException("The compiler library was not found beside mahoc-server.", compilerAssembly);

        return new CompilerServerPackage(serverPath, compilerAssembly, CompilerIdentity.FromCompilerAssembly(compilerAssembly));
    }

    public static CompilerServerResponse Compile(CompilerServerPackage package, CompilerServerRequest request)
    {
        if (request.ProtocolVersion != CompilerProtocol.Version)
            throw new InvalidOperationException("The client used an unsupported compiler-server protocol version.");

        string pipeName = CompilerServerEndpoint.CreatePipeName(package.Compiler);
        if (!TrySend(pipeName, request, 150, out var response))
        {
            StartServer(pipeName, package.ServerExecutablePath);
            if (!TrySend(pipeName, request, 5000, out response))
                throw new IOException("Could not connect to the compiler server after it was started.");
        }

        if (response!.CompilerFingerprint != package.Compiler.Fingerprint)
            throw new InvalidOperationException("The connected compiler server does not match the requested compiler package.");

        return response;
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
        // A daemon must not inherit a test runner's captured stdout/stderr.
        // Such inherited handles keep the runner's output pipe open for the
        // daemon's five-minute idle lifetime, making the parent appear stuck.
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
        using Process process = Process.Start(startInfo) ?? throw new IOException("Failed to launch mahoc-server.");
    }
}
