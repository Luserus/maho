using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Maho.Server.Protocol;

/// <summary>Version of the stable client/server wire contract.</summary>
public static class CompilerProtocol
{
    public const int Version = 1;
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
