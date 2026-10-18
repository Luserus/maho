using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

namespace Maho.Server;

/// <summary>Version of the stable client/server wire contract.</summary>
public static class CompilerProtocol
{
    public const int Version = 1;
}

/// <summary>
/// Version of the newline-delimited JSON contract between the generic daemon
/// and a compiler-package host process. Compiler packages implement this
/// contract without referencing this assembly.
/// </summary>
public static class CompilerHostProtocol
{
    public const int Version = 1;
}

/// <summary>
/// Describes a compiler package that can be hosted by a generic mahoc-server.
/// <see cref="CompilerAssembly"/> is relative to the manifest. A host command
/// starting with <c>./</c> or <c>.\\</c> is resolved relative to the package,
/// allowing a self-contained package-local host executable.
/// </summary>
public sealed record CompilerPackageManifest(
    int FormatVersion,
    string CompilerVersion,
    int CompilerHostProtocolVersion,
    string CompilerAssembly,
    string HostCommand,
    IReadOnlyList<string> HostArguments)
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    public const string FileName = "compiler.json";
    public const int CurrentFormatVersion = 1;

    public static CompilerPackageManifest Load(string packageDirectory)
    {
        string fullPackageDir = Path.GetFullPath(packageDirectory);
        string manifestPath = Path.Combine(fullPackageDir, FileName);
        if (File.Exists(manifestPath))
        {
            var manifest = JsonSerializer.Deserialize<CompilerPackageManifest>(File.ReadAllText(manifestPath), JsonOptions) ?? throw new InvalidDataException("The compiler package manifest was empty.");
            if (manifest.FormatVersion != CurrentFormatVersion)
                throw new InvalidDataException($"Unsupported compiler package format {manifest.FormatVersion}.");
            if (string.IsNullOrWhiteSpace(manifest.CompilerVersion) || string.IsNullOrWhiteSpace(manifest.CompilerAssembly) ||
                string.IsNullOrWhiteSpace(manifest.HostCommand) || manifest.HostArguments is null)
                throw new InvalidDataException("The compiler package manifest is missing required fields.");

            return manifest;
        }

        string compilerAssembly = Path.Combine(fullPackageDir, "Maho.dll");
        if (File.Exists(compilerAssembly))
        {
            var compilerIdentity = CompilerIdentity.FromCompilerAssembly(compilerAssembly);
            string exeName = OperatingSystem.IsWindows() ? "maho-comp-host.exe" : "maho-comp-host";
            string hostExe = Path.Combine(fullPackageDir, exeName);
            string hostDll = Path.Combine(fullPackageDir, "maho-comp-host.dll");
            string oldHostDll = Path.Combine(fullPackageDir, "Maho.CompilerHost.dll");

            string hostCommand;
            IReadOnlyList<string> hostArguments;

            if (File.Exists(hostDll))
            {
                hostCommand = "dotnet";
                hostArguments = ["maho-comp-host.dll"];
            }
            else if (File.Exists(hostExe))
            {
                hostCommand = $"./{exeName}";
                hostArguments = [];
            }
            else if (File.Exists(oldHostDll))
            {
                hostCommand = "dotnet";
                hostArguments = ["Maho.CompilerHost.dll"];
            }
            else
            {
                throw new FileNotFoundException("Could not locate maho-comp-host in package directory.", hostExe);
            }

            return new CompilerPackageManifest(
                CurrentFormatVersion,
                compilerIdentity.Version,
                CompilerHostProtocol.Version,
                "Maho.dll",
                hostCommand,
                hostArguments);
        }

        throw new FileNotFoundException("The compiler package manifest was not found.", manifestPath);
    }
}

[Flags]
public enum CompilerDebugOutput { None = 0, Lexer = 1, Parser = 2, All = Lexer | Parser }
public enum CompilerDiagnosticColorMode : byte { Auto, Always, Never }
public enum CompilerDiagnosticPathStyle : byte { Relative, ProjectRelative, Full }
public enum CompilerDiagnosticFormat : byte { Pretty, Short, Json }
public enum CompilerDiagnosticSeverity : byte { Info, Warning, Error }
public enum CompilerDiagnosticLabelStyle : byte { Primary, Secondary, Context }

public sealed record CompilerOptions
{
    public CompilerDiagnosticColorMode ColorMode { get; init; } = CompilerDiagnosticColorMode.Auto;
    public CompilerDiagnosticPathStyle PathStyle { get; init; } = CompilerDiagnosticPathStyle.Relative;
    public CompilerDiagnosticFormat OutputFormat { get; init; } = CompilerDiagnosticFormat.Pretty;
    public bool WarningsAsErrors { get; init; }
    public string? EntryFile { get; init; }
    public bool ImplicitTopLevel { get; init; }
    public string? RootDirectory { get; init; }
    public IReadOnlyList<string> ReferencedProjects { get; init; } = [];
    public IReadOnlyDictionary<string, string> GlobalAliases { get; init; } = new Dictionary<string, string>();
    public string? ProjectFilePath { get; init; }
    public ulong MacroRecursionLimit { get; init; } = 128;
}

public readonly record struct CompilerTextLocation(int Line, int Column);
public readonly record struct CompilerTextSpan(int Start, int Length, int End, CompilerTextLocation StartLocation, CompilerTextLocation EndLocation);
public readonly record struct CompilerDiagnosticLabel(CompilerTextSpan Span, string? Message, CompilerDiagnosticLabelStyle Style, string? FilePath = null);
public readonly record struct CompilerDiagnosticNote(string Message, string? FilePath = null, CompilerTextSpan? Span = null);
public readonly record struct CompilerDiagnosticHelp(string Message, string? FilePath = null, CompilerTextSpan? Span = null);
public readonly record struct CompilerTextEdit(CompilerTextSpan Span, string NewText, string? FilePath = null);
public sealed record CompilerDiagnosticSuggestion(string Description, IReadOnlyList<CompilerTextEdit> Edits);
public sealed record CompilerMacroTrace(string MacroName, CompilerTextSpan InvocationSpan, string? InvocationFilePath, CompilerTextSpan? DefinitionSpan = null, string? DefinitionFilePath = null, CompilerMacroTrace? Parent = null);

/// <summary>Diagnostics are wire DTOs so CLI and editor clients do not load compiler assemblies.</summary>
public sealed record CompilerDiagnostic(
    string Code,
    string Message,
    CompilerDiagnosticSeverity Severity,
    CompilerTextSpan Span,
    string? SourcePath = null,
    IReadOnlyList<CompilerDiagnosticLabel>? Labels = null,
    IReadOnlyList<CompilerDiagnosticNote>? Notes = null,
    IReadOnlyList<CompilerDiagnosticHelp>? HelpMessages = null,
    IReadOnlyList<CompilerDiagnosticSuggestion>? Suggestions = null,
    CompilerMacroTrace? MacroTrace = null)
{
    public IReadOnlyList<CompilerDiagnosticLabel> Labels { get; init; } = Labels ?? [];
    public IReadOnlyList<CompilerDiagnosticNote> Notes { get; init; } = Notes ?? [];
    public IReadOnlyList<CompilerDiagnosticHelp> HelpMessages { get; init; } = HelpMessages ?? [];
    public IReadOnlyList<CompilerDiagnosticSuggestion> Suggestions { get; init; } = Suggestions ?? [];
}

public sealed record CompilerServerRequest(
    int ProtocolVersion,
    string ProjectName,
    string ModuleName,
    string RootDirectory,
    string[] SourcePaths,
    CompilerOptions Options,
    CompilerDebugOutput DebugOutput,
    bool CheckOnly);

public sealed record CompilerServerFileResult(string SourcePath, string? LexerJson, string? ParserJson,
    IReadOnlyList<CompilerDiagnostic> Diagnostics, string? AnalysisError, bool IsInternalError);

public sealed record CompilerServerResponse(string ProjectName, CompilerServerFileResult[] Files, string? EntryFile,
    long ElapsedTicks, bool HasErrors, bool LaterStagesNotImplemented, string CompilerVersion, string CompilerFingerprint);

/// <summary>Control message carried over a compiler daemon pipe.</summary>
public sealed record CompilerServerControlRequest(int ProtocolVersion, string Method);
public sealed record CompilerServerControlResponse(bool Success, string? Message = null);

/// <summary>
/// Tracks live daemon endpoints for the current user. The registry is only
/// discovery metadata: every control operation still authenticates itself by
/// connecting to the user-specific named pipe.
/// </summary>
public sealed record CompilerServerRegistration(string PipeName, string CompilerVersion, string CompilerFingerprint);

public static class CompilerServerRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string DirectoryPath
    {
        get
        {
            string userHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName))).ToLowerInvariant()[..16];
            return Path.Combine(Path.GetTempPath(), "maho", "compiler-servers", userHash);
        }
    }

    public static void Register(CompilerServerRegistration registration)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath, registration.PipeName + ".json"), JsonSerializer.Serialize(registration, JsonOptions));
    }

    public static void Unregister(string pipeName)
    {
        string path = Path.Combine(DirectoryPath, pipeName + ".json");
        if (File.Exists(path))
            File.Delete(path);
    }

    public static IReadOnlyList<CompilerServerRegistration> GetRegistrations()
    {
        if (!Directory.Exists(DirectoryPath))
            return [];

        var registrations = new List<CompilerServerRegistration>();
        foreach (string path in Directory.GetFiles(DirectoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var registration = JsonSerializer.Deserialize<CompilerServerRegistration>(File.ReadAllText(path), JsonOptions);
                if (registration is not null && !string.IsNullOrWhiteSpace(registration.PipeName))
                    registrations.Add(registration);
            }
            catch (JsonException)
            {
                File.Delete(path);
            }
        }
        return registrations;
    }
}

/// <summary>Identity of the compiler package hosting a server daemon.</summary>
public sealed record CompilerIdentity(string Version, string Fingerprint)
{
    public static CompilerIdentity FromCompilerAssembly(string compilerAssemblyPath)
    {
        string fullPath = Path.GetFullPath(compilerAssemblyPath);
        AssemblyName name = AssemblyName.GetAssemblyName(fullPath);
        string version = name.Version?.ToString(3) ?? "0.0.0";
        using var stream = File.OpenRead(fullPath);
        using var peReader = new PEReader(stream);
        MetadataReader metadata = peReader.GetMetadataReader();
        string moduleVersionId = metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString("N");
        return new CompilerIdentity(version, moduleVersionId);
    }

    public static CompilerIdentity FromCompilerAssembly(Assembly compilerAssembly)
    {
        string version = compilerAssembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return new CompilerIdentity(version, compilerAssembly.ManifestModule.ModuleVersionId.ToString("N"));
    }
}

/// <summary>Builds a user- and compiler-specific daemon endpoint without environment configuration.</summary>
public static class CompilerServerEndpoint
{
    public static string CreatePipeName(CompilerIdentity compiler)
    {
        string user = Environment.UserName;
        string identity = $"{CompilerProtocol.Version}|{user}|{compiler.Version}|{compiler.Fingerprint}";
        string suffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()[..20];
        return $"mahoc-{CompilerProtocol.Version}-{suffix}";
    }
}
