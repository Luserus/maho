using System.Collections.Generic;
using Maho.Diagnostics;
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
    IReadOnlyDictionary<string, object?>? CustomData = null)
{
    public IReadOnlyList<DiagnosticLabelInfo> Labels { get; init; } = Labels ?? [];
    public IReadOnlyList<DiagnosticNoteInfo> Notes { get; init; } = Notes ?? [];
    public IReadOnlyList<DiagnosticHelpInfo> HelpMessages { get; init; } = HelpMessages ?? [];
    public IReadOnlyList<DiagnosticSuggestionInfo> Suggestions { get; init; } = Suggestions ?? [];
    public IReadOnlyDictionary<string, object?> CustomData { get; init; } = CustomData ?? EmptyCustomData;

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
            diagnostic.CustomData.Count > 0 ? diagnostic.CustomData : null);
    }

    private static DiagnosticSeverity ToSeverity(DiagnosticKind kind) => kind switch
    {
        DiagnosticKind.Error => DiagnosticSeverity.Error,
        DiagnosticKind.Warning => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Info
    };
}
