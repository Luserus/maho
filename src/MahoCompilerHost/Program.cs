using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Maho.CompilerHost;

/// <summary>
/// Package-local adapter for one compiler build. Its stdin/stdout contract is
/// JSON only: it deliberately does not reference the server protocol assembly.
/// </summary>
internal static class Program
{
    private const int HostProtocolVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static int Main()
    {
        string? line;
        while ((line = Console.In.ReadLine()) is not null)
        {
            string id = "<unknown>";
            try
            {
                var request = JsonSerializer.Deserialize<HostRequest>(line, JsonOptions)
                    ?? throw new InvalidDataException("The compiler-host request was empty.");
                id = request.Id;

                object result = request.Method switch
                {
                    "hello" => Hello(request.Params),
                    "compile" => Compile(request.Params),
                    _ => throw new InvalidDataException($"Unknown compiler-host method '{request.Method}'.")
                };
                Write(new HostResponse(id, true, result, null));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Maho.CompilerHost: {ex.Message}");
                Write(new HostResponse(id, false, null, ex.Message));
            }
        }
        return 0;
    }

    private static HostHello Hello(JsonElement parameters)
    {
        int requestedVersion = parameters.GetProperty("protocolVersion").GetInt32();
        if (requestedVersion != HostProtocolVersion)
            throw new InvalidDataException($"Compiler host protocol {requestedVersion} is not supported.");

        Assembly assembly = typeof(MahoCompiler).Assembly;
        return new HostHello(HostProtocolVersion, assembly.GetName().Version?.ToString(3) ?? "0.0.0", assembly.ManifestModule.ModuleVersionId.ToString("N"));
    }

    private static HostCompileResult Compile(JsonElement parameters)
    {
        var request = JsonSerializer.Deserialize<HostCompileRequest>(parameters.GetRawText(), JsonOptions)
            ?? throw new InvalidDataException("The compilation request was empty.");
        var options = new CompilationOptions
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

        var compilation = MahoCompiler.AnalyzeFiles(request.SourcePaths, request.RootDirectory, options);
        var filesList = compilation.SyntaxTrees.Select(tree => new HostFileResult(
            tree.FilePath,
            null,
            null,
            tree.Diagnostics.Select(ToWireDiagnostic).ToArray(),
            null,
            false)).ToList();

        foreach (var (path, (errorMsg, isInternal)) in compilation.FileErrors)
        {
            filesList.Add(new HostFileResult(path, null, null, [], errorMsg, isInternal));
        }

        Assembly assembly = typeof(MahoCompiler).Assembly;
        return new HostCompileResult(request.ProjectName, filesList.ToArray(), compilation.EntryFile, compilation.Elapsed.Ticks,
            compilation.HasErrors, !request.CheckOnly && !compilation.HasErrors,
            assembly.GetName().Version?.ToString(3) ?? "0.0.0", assembly.ManifestModule.ModuleVersionId.ToString("N"));
    }

    private static HostDiagnostic ToWireDiagnostic(DiagnosticInfo diagnostic) => new(
        diagnostic.Code, diagnostic.Message, (byte)diagnostic.Severity, ToWireSpan(diagnostic.Span), diagnostic.SourcePath,
        diagnostic.Labels.Select(label => new HostDiagnosticLabel(ToWireSpan(label.Span), label.Message, (byte)label.Style, label.FilePath)).ToArray(),
        diagnostic.Notes.Select(note => new HostDiagnosticNote(note.Message, note.FilePath, note.Span is { } span ? ToWireSpan(span) : null)).ToArray(),
        diagnostic.HelpMessages.Select(help => new HostDiagnosticHelp(help.Message, help.FilePath, help.Span is { } span ? ToWireSpan(span) : null)).ToArray(),
        diagnostic.Suggestions.Select(suggestion => new HostDiagnosticSuggestion(suggestion.Description,
            suggestion.Edits.Select(edit => new HostTextEdit(ToWireSpan(edit.Span), edit.NewText, edit.FilePath)).ToArray())).ToArray(),
        diagnostic.MacroTrace is null ? null : ToWireMacroTrace(diagnostic.MacroTrace));

    private static HostMacroTrace ToWireMacroTrace(MacroExpansionTraceInfo trace) => new(trace.MacroName,
        ToWireSpan(trace.InvocationSpan), trace.InvocationFilePath,
        trace.DefinitionSpan is { } span ? ToWireSpan(span) : null, trace.DefinitionFilePath,
        trace.Parent is null ? null : ToWireMacroTrace(trace.Parent));

    private static HostTextSpan ToWireSpan(TextSpanInfo span) => new(span.Start, span.Length, span.End,
        new HostTextLocation(span.StartLocation.Line, span.StartLocation.Column),
        new HostTextLocation(span.EndLocation.Line, span.EndLocation.Column));

    private static void Write(HostResponse response) => Console.Out.WriteLine(JsonSerializer.Serialize(response, JsonOptions));

    private sealed record HostRequest(string Id, string Method, JsonElement Params);
    private sealed record HostResponse(string Id, bool Ok, object? Result, string? Error);
    private sealed record HostHello(int CompilerHostProtocolVersion, string CompilerVersion, string CompilerFingerprint);

    private sealed record HostCompileRequest(
        int ProtocolVersion,
        string ProjectName,
        string ModuleName,
        string RootDirectory,
        string[] SourcePaths,
        HostCompilerOptions Options,
        int DebugOutput,
        bool CheckOnly);

    private sealed record HostCompilerOptions
    {
        public byte ColorMode { get; init; }
        public byte PathStyle { get; init; }
        public byte OutputFormat { get; init; }
        public bool WarningsAsErrors { get; init; }
        public string? EntryFile { get; init; }
        public bool ImplicitTopLevel { get; init; }
        public string? RootDirectory { get; init; }
        public IReadOnlyList<string> ReferencedProjects { get; init; } = [];
        public IReadOnlyDictionary<string, string> GlobalAliases { get; init; } = new Dictionary<string, string>();
        public string? ProjectFilePath { get; init; }
        public ulong MacroRecursionLimit { get; init; } = 128;
    }

    private readonly record struct HostTextLocation(int Line, int Column);
    private readonly record struct HostTextSpan(int Start, int Length, int End, HostTextLocation StartLocation, HostTextLocation EndLocation);
    private readonly record struct HostDiagnosticLabel(HostTextSpan Span, string? Message, byte Style, string? FilePath = null);
    private readonly record struct HostDiagnosticNote(string Message, string? FilePath = null, HostTextSpan? Span = null);
    private readonly record struct HostDiagnosticHelp(string Message, string? FilePath = null, HostTextSpan? Span = null);
    private readonly record struct HostTextEdit(HostTextSpan Span, string NewText, string? FilePath = null);
    private sealed record HostDiagnosticSuggestion(string Description, IReadOnlyList<HostTextEdit> Edits);
    private sealed record HostMacroTrace(string MacroName, HostTextSpan InvocationSpan, string? InvocationFilePath, HostTextSpan? DefinitionSpan = null, string? DefinitionFilePath = null, HostMacroTrace? Parent = null);
    private sealed record HostDiagnostic(string Code, string Message, byte Severity, HostTextSpan Span, string? SourcePath,
        IReadOnlyList<HostDiagnosticLabel> Labels, IReadOnlyList<HostDiagnosticNote> Notes, IReadOnlyList<HostDiagnosticHelp> HelpMessages,
        IReadOnlyList<HostDiagnosticSuggestion> Suggestions, HostMacroTrace? MacroTrace);
    private sealed record HostFileResult(string SourcePath, string? LexerJson, string? ParserJson, IReadOnlyList<HostDiagnostic> Diagnostics, string? AnalysisError, bool IsInternalError);
    private sealed record HostCompileResult(string ProjectName, HostFileResult[] Files, string? EntryFile, long ElapsedTicks,
        bool HasErrors, bool LaterStagesNotImplemented, string CompilerVersion, string CompilerFingerprint);
}
