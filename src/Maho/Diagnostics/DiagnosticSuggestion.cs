using System.Collections.Generic;
using Maho.Text;

namespace Maho.Diagnostics;

/// <summary>
/// A single text replacement edit within a source file.
/// </summary>
internal readonly record struct TextEdit(
    TextSpan Span,
    string NewText,
    SourceText? Source = null
);

/// <summary>
/// Indicates the safety and certainty of an automated diagnostic code suggestion.
/// </summary>
public enum SuggestionApplicability : byte
{
    /// <summary> The suggestion is completely machine-applicable and safe to apply automatically. </summary>
    MachineApplicable,

    /// <summary> The suggestion might be what the author intended, but may require human review. </summary>
    MaybeIncorrect,

    /// <summary> The suggestion contains placeholders that must be filled in by the author. </summary>
    HasPlaceholders
}

/// <summary>
/// A multi-edit, multi-span code fix suggestion attached to a diagnostic.
/// </summary>
internal sealed class DiagnosticSuggestion
{
    public string Description { get; }
    public IReadOnlyList<TextEdit> Edits { get; }
    public SuggestionApplicability Applicability { get; }

    public DiagnosticSuggestion(
        string description,
        IReadOnlyList<TextEdit> edits,
        SuggestionApplicability applicability = SuggestionApplicability.MachineApplicable)
    {
        Description = description;
        Edits = edits;
        Applicability = applicability;
    }

    public DiagnosticSuggestion(
        string description,
        TextEdit singleEdit,
        SuggestionApplicability applicability = SuggestionApplicability.MachineApplicable)
        : this(description, [singleEdit], applicability)
    {
    }
}
