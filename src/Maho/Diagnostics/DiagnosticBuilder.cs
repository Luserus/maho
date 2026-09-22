using System.Collections.Generic;
using Maho.Text;

namespace Maho.Diagnostics;

/// <summary>
/// Fluent builder for constructing rich, multi-span diagnostics before reporting them to a <see cref="DiagnosticsManager"/>.
/// </summary>
internal sealed class DiagnosticBuilder
{
    private readonly DiagnosticsManager manager;
    private readonly string code;
    private readonly string message;
    private readonly DiagnosticKind kind;
    private readonly TextSpan primarySpan;
    private readonly SourceText? defaultSource;

    private List<DiagnosticLabel>? labels;
    private List<DiagnosticNote>? notes;
    private List<DiagnosticHelp>? helpMessages;
    private List<DiagnosticSuggestion>? suggestions;
    private Dictionary<string, object?>? customData;

    public DiagnosticBuilder(
        DiagnosticsManager manager,
        string code,
        string message,
        DiagnosticKind kind,
        TextSpan primarySpan,
        SourceText? defaultSource = null)
    {
        this.manager = manager;
        this.code = code;
        this.message = message;
        this.kind = kind;
        this.primarySpan = primarySpan;
        this.defaultSource = defaultSource;
    }

    /// <summary> Adds an annotated label with primary priority (rendered with ^^^^ in severity color). </summary>
    public DiagnosticBuilder WithPrimaryLabel(TextSpan span, string? message = null, SourceText? source = null) =>
        WithLabel(new DiagnosticLabel(span, message, DiagnosticLabelStyle.Primary, source ?? defaultSource));

    /// <summary> Adds an annotated label with secondary priority (rendered with ---- in context color). </summary>
    public DiagnosticBuilder WithSecondaryLabel(TextSpan span, string? message = null, SourceText? source = null) =>
        WithLabel(new DiagnosticLabel(span, message, DiagnosticLabelStyle.Secondary, source ?? defaultSource));

    /// <summary> Adds an explicit <see cref="DiagnosticLabel"/>. </summary>
    public DiagnosticBuilder WithLabel(DiagnosticLabel label)
    {
        labels ??= [];
        labels.Add(label);
        return this;
    }

    /// <summary> Adds a contextual note to the diagnostic. </summary>
    public DiagnosticBuilder WithNote(string message, SourceText? source = null, TextSpan? span = null)
    {
        notes ??= [];
        notes.Add(new DiagnosticNote(message, source ?? defaultSource, span));
        return this;
    }

    /// <summary> Adds actionable remediation advice or a hint. </summary>
    public DiagnosticBuilder WithHelp(string message, SourceText? source = null, TextSpan? span = null)
    {
        helpMessages ??= [];
        helpMessages.Add(new DiagnosticHelp(message, source ?? defaultSource, span));
        return this;
    }

    /// <summary> Adds a multi-edit code suggestion. </summary>
    public DiagnosticBuilder WithSuggestion(DiagnosticSuggestion suggestion)
    {
        suggestions ??= [];
        suggestions.Add(suggestion);
        return this;
    }

    /// <summary> Adds a single-replacement code suggestion. </summary>
    public DiagnosticBuilder WithSuggestion(
        string description,
        TextSpan span,
        string replacementText,
        SourceText? source = null,
        SuggestionApplicability applicability = SuggestionApplicability.MachineApplicable)
    {
        return WithSuggestion(new DiagnosticSuggestion(
            description,
            new TextEdit(span, replacementText, source ?? defaultSource),
            applicability));
    }

    /// <summary> Adds a multi-edit code suggestion with a collection of edits. </summary>
    public DiagnosticBuilder WithSuggestion(
        string description,
        IReadOnlyList<TextEdit> edits,
        SuggestionApplicability applicability = SuggestionApplicability.MachineApplicable)
    {
        return WithSuggestion(new DiagnosticSuggestion(description, edits, applicability));
    }

    /// <summary> Attaches arbitrary metadata for external tooling or IDEs. </summary>
    public DiagnosticBuilder WithCustomData(string key, object? value)
    {
        customData ??= [];
        customData[key] = value;
        return this;
    }

    /// <summary> Materializes the configured <see cref="Diagnostic"/> instance. </summary>
    public Diagnostic Build()
    {
        return new Diagnostic(code, message, primarySpan, kind, source: defaultSource)
        {
            Labels = labels ?? (IReadOnlyList<DiagnosticLabel>)[],
            Notes = notes ?? (IReadOnlyList<DiagnosticNote>)[],
            HelpMessages = helpMessages ?? (IReadOnlyList<DiagnosticHelp>)[],
            Suggestions = suggestions ?? (IReadOnlyList<DiagnosticSuggestion>)[],
            CustomData = customData ?? (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>()
        };
    }

    /// <summary> Builds the diagnostic and appends it to the target <see cref="DiagnosticsManager"/>. </summary>
    public void Report() => manager.Report(Build());
}
