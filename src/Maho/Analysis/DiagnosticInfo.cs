using System;
using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Syntax;
using Maho.Text;

namespace Maho;

/// <summary> Public diagnostic payload exposed to API consumers. </summary>
/// <param name="Code">Stable diagnostic identifier intended for tooling and tests.</param>
/// <param name="Message">Human-readable description of the problem.</param>
/// <param name="Severity">Normalized severity category for the problem.</param>
/// <param name="Span">Location data expressed as both offsets and line/column endpoints.</param>
/// <param name="ExpectedText">Expected syntax fragment when the diagnostic represents a missing item.</param>
/// <param name="SourcePath">Optional path to the file where the diagnostic occurred.</param>
/// <param name="Labels">Underline and annotation labels for multi-span diagnostics.</param>
/// <param name="Notes">Informational notes.</param>
/// <param name="HelpMessages">Help or hint messages.</param>
/// <param name="Suggestions">Suggested code fixes.</param>
/// <param name="CustomData">Arbitrary key-value metadata for tooling.</param>
/// <param name="MacroTrace">Macro expansion provenance when the diagnostic occurs in expanded code.</param>
public sealed record DiagnosticInfo(
    string Code,
    string Message,
    DiagnosticSeverity Severity,
    TextSpanInfo Span,
    string? ExpectedText = null,
    string? SourcePath = null,
    IReadOnlyList<DiagnosticLabelInfo>? Labels = null,
    IReadOnlyList<DiagnosticNoteInfo>? Notes = null,
    IReadOnlyList<DiagnosticHelpInfo>? HelpMessages = null,
    IReadOnlyList<DiagnosticSuggestionInfo>? Suggestions = null,
    IReadOnlyDictionary<string, object?>? CustomData = null,
    MacroExpansionTraceInfo? MacroTrace = null)
{
    public IReadOnlyList<DiagnosticLabelInfo> Labels { get; init; } = Labels ?? [];
    public IReadOnlyList<DiagnosticNoteInfo> Notes { get; init; } = Notes ?? [];
    public IReadOnlyList<DiagnosticHelpInfo> HelpMessages { get; init; } = HelpMessages ?? [];
    public IReadOnlyList<DiagnosticSuggestionInfo> Suggestions { get; init; } = Suggestions ?? [];
    public IReadOnlyDictionary<string, object?> CustomData { get; init; } = CustomData ?? EmptyCustomData;
    public MacroExpansionTraceInfo? MacroTrace { get; init; } = MacroTrace;

    private static readonly IReadOnlyDictionary<string, object?> EmptyCustomData = new Dictionary<string, object?>();

    /// <summary>
    /// Projects an internal <see cref="Diagnostic"/> into a public <see cref="DiagnosticInfo"/> DTO.
    /// </summary>
    internal static DiagnosticInfo FromDiagnostic(Diagnostic diagnostic, SourceText? defaultSource = null, string? defaultPath = null)
    {
        var primarySource = diagnostic.Source ?? defaultSource;
        var primaryPath = diagnostic.Source?.FilePath ?? defaultPath;
        var primarySpanInfo = TextSpanInfo.FromSpan(diagnostic.PrimarySpan, primarySource);

        List<DiagnosticLabelInfo>? labels = null;

        if (diagnostic.Labels.Count > 0)
        {
            labels = new List<DiagnosticLabelInfo>(diagnostic.Labels.Count);

            foreach (var label in diagnostic.Labels)
            {
                var labelSource = label.Source ?? primarySource;
                var labelPath = label.Source?.FilePath ?? primaryPath;
                labels.Add(new DiagnosticLabelInfo(
                    TextSpanInfo.FromSpan(label.Span, labelSource),
                    label.Message,
                    label.Style,
                    labelPath));
            }
        }

        List<DiagnosticNoteInfo>? notes = null;

        if (diagnostic.Notes.Count > 0)
        {
            notes = new List<DiagnosticNoteInfo>(diagnostic.Notes.Count);

            foreach (var note in diagnostic.Notes)
            {
                var noteSource = note.Source ?? primarySource;
                var notePath = note.Source?.FilePath ?? primaryPath;
                var noteSpan = note.Span.HasValue ? TextSpanInfo.FromSpan(note.Span.Value, noteSource) : (TextSpanInfo?)null;
                notes.Add(new DiagnosticNoteInfo(note.Message, notePath, noteSpan));
            }
        }

        List<DiagnosticHelpInfo>? helpMessages = null;

        if (diagnostic.HelpMessages.Count > 0)
        {
            helpMessages = new List<DiagnosticHelpInfo>(diagnostic.HelpMessages.Count);

            foreach (var help in diagnostic.HelpMessages)
            {
                var helpSource = help.Source ?? primarySource;
                var helpPath = help.Source?.FilePath ?? primaryPath;
                var helpSpan = help.Span.HasValue ? TextSpanInfo.FromSpan(help.Span.Value, helpSource) : (TextSpanInfo?)null;
                helpMessages.Add(new DiagnosticHelpInfo(help.Message, helpPath, helpSpan));
            }
        }

        List<DiagnosticSuggestionInfo>? suggestions = null;
        if (diagnostic.Suggestions.Count > 0)
        {
            suggestions = new List<DiagnosticSuggestionInfo>(diagnostic.Suggestions.Count);

            foreach (var suggestion in diagnostic.Suggestions)
            {
                var edits = new List<TextEditInfo>(suggestion.Edits.Count);

                foreach (var edit in suggestion.Edits)
                {
                    var editSource = edit.Source ?? primarySource;
                    var editPath = edit.Source?.FilePath ?? primaryPath;
                    edits.Add(new TextEditInfo(
                        TextSpanInfo.FromSpan(edit.Span, editSource),
                        edit.NewText,
                        editPath));
                }

                suggestions.Add(new DiagnosticSuggestionInfo(suggestion.Description, edits, suggestion.Applicability));
            }
        }

        MacroExpansionTraceInfo? macroTrace = null;

        if (diagnostic.ExpansionOrigin is not null)
            macroTrace = CreateMacroTraceInfo(diagnostic.ExpansionOrigin, primarySource, primaryPath);

        return new DiagnosticInfo(
            diagnostic.DiagnosticCode,
            diagnostic.Message,
            ToSeverity(diagnostic.Kind),
            primarySpanInfo,
            diagnostic.ExpectedText,
            primaryPath,
            labels,
            notes,
            helpMessages,
            suggestions,
            diagnostic.CustomData.Count > 0 ? diagnostic.CustomData : null,
            macroTrace);
    }

    private static MacroExpansionTraceInfo CreateMacroTraceInfo(ExpansionOrigin origin, SourceText? fallbackSource, string? fallbackPath)
    {
        var invSource = origin.InvocationSource ?? fallbackSource;
        var invPath = origin.InvocationSource?.FilePath ?? fallbackPath;
        var invSpan = TextSpanInfo.FromSpan(origin.InvocationSpan, invSource);

        TextSpanInfo? defSpan = null;
        string? defPath = null;

        if (origin.DefinitionSpan.HasValue)
        {
            var defSource = origin.DefinitionSource ?? fallbackSource;
            defPath = origin.DefinitionSource?.FilePath ?? fallbackPath;
            defSpan = TextSpanInfo.FromSpan(origin.DefinitionSpan.Value, defSource);
        }

        var parentTrace = origin.Parent is not null ? CreateMacroTraceInfo(origin.Parent, fallbackSource, fallbackPath) : null;

        return new MacroExpansionTraceInfo(
            origin.MacroName.ToString() ?? string.Empty,
            invSpan,
            invPath,
            defSpan,
            defPath,
            parentTrace);
    }

    private static DiagnosticSeverity ToSeverity(DiagnosticKind kind) => kind switch
    {
        DiagnosticKind.Error => DiagnosticSeverity.Error,
        DiagnosticKind.Warning => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Info
    };

    /// <summary>
    /// Gets the compiler pipeline stage priority for ordering diagnostics:
    /// Lexer (1) > Parser (2) > Resolver (3) > Other (4).
    /// </summary>
    public static int GetPipelineStagePriority(string code)
    {
        if (code.StartsWith("MH", System.StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(code.AsSpan(2), out int num))
        {
            if (num is >= 100 and <= 119)
                return 1; // Lexer
            if (num is >= 120 and <= 499)
                return 2; // Parser
            if (num >= 500)
                return 3; // Resolver
        }

        return 4;
    }

    /// <summary>
    /// Compares two diagnostics for sequential source ordering per file.
    /// Orders by line number, then pipeline stage priority (Lexer > Parser > Resolver) if on the same line,
    /// then column number.
    /// </summary>
    public static int CompareBySourceOrder(DiagnosticInfo a, DiagnosticInfo b)
    {
        // 1. Line number ascending
        int lineCmp = a.Span.StartLocation.Line.CompareTo(b.Span.StartLocation.Line);

        if (lineCmp != 0)
            return lineCmp;

        // 2. If same line: Report priority Lexer > Parser > Resolver
        int prioA = GetPipelineStagePriority(a.Code);
        int prioB = GetPipelineStagePriority(b.Code);
        int prioCmp = prioA.CompareTo(prioB);

        if (prioCmp != 0)
            return prioCmp;

        // 3. Column number ascending
        int colCmp = a.Span.StartLocation.Column.CompareTo(b.Span.StartLocation.Column);

        if (colCmp != 0)
            return colCmp;

        // 4. Span start offset ascending
        return a.Span.Start.CompareTo(b.Span.Start);
    }

    /// <summary>
    /// Orders diagnostics grouped by file, with diagnostics in each file ordered sequentially
    /// by source position (line, column) and pipeline stage priority (Lexer > Parser > Resolver).
    /// </summary>
    public static List<DiagnosticInfo> OrderDiagnostics(IEnumerable<DiagnosticInfo> diagnostics)
    {
        var result = new List<DiagnosticInfo>();
        var groupedByFile = new Dictionary<string, List<DiagnosticInfo>>(System.StringComparer.OrdinalIgnoreCase);
        var fileOrder = new List<string>();

        foreach (var diag in diagnostics)
        {
            string key = diag.SourcePath ?? string.Empty;

            if (!groupedByFile.TryGetValue(key, out var list))
            {
                list = new List<DiagnosticInfo>();
                groupedByFile[key] = list;
                fileOrder.Add(key);
            }

            list.Add(diag);
        }

        foreach (var fileKey in fileOrder)
        {
            var list = groupedByFile[fileKey];
            list.Sort(CompareBySourceOrder);
            result.AddRange(list);
        }

        return result;
    }
}
