using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Maho.Server.Protocol;

namespace Maho.Server;

/// <summary>Hosts sessions for one user and one compiler build.</summary>
public static class CompilerServerHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static int Run(string[] args)
    {
        if (!TryParseArguments(args, out string suppliedPipeName, out TimeSpan? idleTimeout, out bool oneShot))
            return 1;

        CompilerIdentity compiler = CompilerIdentity.FromCompilerAssembly(typeof(MahoCompiler).Assembly);
        string expectedPipeName = CompilerServerEndpoint.CreatePipeName(compiler);

        if (!string.Equals(suppliedPipeName, expectedPipeName, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("mahoc-server: the pipe does not belong to this compiler build.");
            return 1;
        }

        var sessions = new Dictionary<string, CompilerSession>(StringComparer.Ordinal);
        bool servedRequest = false;

        try
        {
            while (true)
            {
                PruneExpiredSessions(sessions, idleTimeout);

                if (servedRequest && sessions.Count == 0)
                    return 0;

                using var pipe = new NamedPipeServerStream(expectedPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                using var cancellation = CreateAcceptCancellation(sessions, idleTimeout, servedRequest);

                try
                {
                    pipe.WaitForConnectionAsync(cancellation.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    continue;
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
                    if (request.ProtocolVersion != CompilerProtocol.Version)
                        throw new InvalidDataException("Compiler request uses an unsupported protocol version.");

                    var session = GetOrCreateSession(sessions, CreateSessionKey(request), request);
                    session.ActiveRequests++;
                    try
                    {
                        response = Compile(request, compiler);
                        session.LastAccessUtc = DateTimeOffset.UtcNow;
                    }
                    finally
                    {
                        session.ActiveRequests--;
                    }
                    servedRequest = true;
                }
                catch (Exception ex)
                {
                    response = new CompilerServerResponse("<unknown>", [], null, 0, true, false, compiler.Version, compiler.Fingerprint);
                    Console.Error.WriteLine($"mahoc-server: {ex.Message}");
                }

                writer.WriteLine(JsonSerializer.Serialize(response, JsonOptions));

                if (oneShot)
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"mahoc-server: {ex.Message}");
            return 1;
        }
    }

    private static CompilerServerResponse Compile(CompilerServerRequest request, CompilerIdentity compiler)
    {
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
        var analysis = MahoCompiler.AnalyzeFiles(request.SourcePaths, (AnalysisOutput)request.DebugOutput, request.RootDirectory, options);
        var files = analysis.Files.Select(file => new CompilerServerFileResult(file.SourcePath, file.DebugOutput?.LexerJson,
            file.DebugOutput?.ParserJson, file.Output?.Diagnostics.Select(ToWireDiagnostic).ToArray() ?? [], file.AnalysisError, file.IsInternalError)).ToArray();

        return new CompilerServerResponse(request.ProjectName, files, analysis.EntryFile, analysis.Elapsed.Ticks,
            analysis.HasErrors, !request.CheckOnly && !analysis.HasErrors, compiler.Version, compiler.Fingerprint);
    }

    private static CompilerDiagnostic ToWireDiagnostic(DiagnosticInfo diagnostic) => new(
        diagnostic.Code, diagnostic.Message, (CompilerDiagnosticSeverity)diagnostic.Severity, ToWireSpan(diagnostic.Span), diagnostic.SourcePath,
        diagnostic.Labels.Select(label => new CompilerDiagnosticLabel(ToWireSpan(label.Span), label.Message, (CompilerDiagnosticLabelStyle)label.Style, label.FilePath)).ToArray(),
        diagnostic.Notes.Select(note => new CompilerDiagnosticNote(note.Message, note.FilePath, note.Span is { } span ? ToWireSpan(span) : null)).ToArray(),
        diagnostic.HelpMessages.Select(help => new CompilerDiagnosticHelp(help.Message, help.FilePath, help.Span is { } span ? ToWireSpan(span) : null)).ToArray(),
        diagnostic.Suggestions.Select(suggestion => new CompilerDiagnosticSuggestion(suggestion.Description,
            suggestion.Edits.Select(edit => new CompilerTextEdit(ToWireSpan(edit.Span), edit.NewText, edit.FilePath)).ToArray())).ToArray(),
        diagnostic.MacroTrace is null ? null : ToWireMacroTrace(diagnostic.MacroTrace));

    private static CompilerMacroTrace ToWireMacroTrace(MacroExpansionTraceInfo trace) => new(trace.MacroName,
        ToWireSpan(trace.InvocationSpan), trace.InvocationFilePath,
        trace.DefinitionSpan is { } span ? ToWireSpan(span) : null, trace.DefinitionFilePath,
        trace.Parent is null ? null : ToWireMacroTrace(trace.Parent));

    private static CompilerTextSpan ToWireSpan(TextSpanInfo span) => new(span.Start, span.Length, span.End,
        new CompilerTextLocation(span.StartLocation.Line, span.StartLocation.Column),
        new CompilerTextLocation(span.EndLocation.Line, span.EndLocation.Column));

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

    private static bool TryParseArguments(string[] args, out string pipeName, out TimeSpan? timeout, out bool oneShot)
    {
        pipeName = string.Empty;
        timeout = TimeSpan.FromMinutes(5);
        oneShot = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pipe" when i + 1 < args.Length: pipeName = args[++i]; break;
                case "--idle-timeout" when i + 1 < args.Length && int.TryParse(args[++i], out int seconds) && seconds >= 0:
                    oneShot = seconds == 0;
                    timeout = oneShot ? null : TimeSpan.FromSeconds(seconds);
                    break;
                case "--no-timeout": timeout = null; oneShot = false; break;
                default:
                    Console.Error.WriteLine("Usage: mahoc-server --pipe <name> [--idle-timeout <seconds>|--no-timeout]");
                    return false;
            }
        }
        return !string.IsNullOrWhiteSpace(pipeName);
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
}
