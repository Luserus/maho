using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Maho.Server;

/// <summary>Version of the stable client/server wire contract.</summary>
public static class CompilerProtocol
{
    public const int Version = 1;
}

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

/// <summary>Diagnostics are wire DTOs so CLI and editor clients do not need to directly reference the compiler assembly.</summary>
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
    bool CheckOnly);

public sealed record CompilerServerFileResult(string SourcePath,
    IReadOnlyList<CompilerDiagnostic> Diagnostics, string? AnalysisError, bool IsInternalError);

public sealed record CompilerServerResponse(string ProjectName, CompilerServerFileResult[] Files, string? EntryFile,
    long ElapsedTicks, bool HasErrors, bool LaterStagesNotImplemented, string CompilerVersion, string CompilerFingerprint);

/// <summary>Control message carried over a compiler daemon pipe.</summary>
public sealed record CompilerServerControlRequest(int ProtocolVersion, string Method);
public sealed record CompilerServerControlResponse(bool Success, string? Message = null);

/// <summary>
/// Tracks live daemon endpoints for the current user.
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
    public static CompilerIdentity ForVersion(string version)
    {
        string norm = CompilerServerEndpoint.NormalizeVersion(version);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(norm))).ToLowerInvariant()[..16];
        return new CompilerIdentity(norm, hash);
    }
}

/// <summary>Builds user- and compiler-specific daemon endpoints.</summary>
public static class CompilerServerEndpoint
{
    public static string NormalizeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "0.2.0";
        version = version.Trim();
        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase)) version = version[1..].Trim();
        if (Version.TryParse(version, out var parsed))
        {
            int build = parsed.Build >= 0 ? parsed.Build : 0;
            return $"{parsed.Major}.{Math.Max(0, parsed.Minor)}.{build}";
        }
        return version;
    }

    public static string CreatePipeName(string version)
    {
        string norm = NormalizeVersion(version);
        string user = Environment.UserName;
        string identity = $"{CompilerProtocol.Version}|{user}|{norm}";
        string suffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()[..16];
        return $"maho-server-{norm}-{suffix}";
    }

    public static string CreatePipeName(CompilerIdentity compiler) => CreatePipeName(compiler.Version);
}
