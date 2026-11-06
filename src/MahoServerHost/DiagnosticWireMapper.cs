using System.Linq;

namespace Maho.Server;

internal static class DiagnosticWireMapper
{
    public static CompilerDiagnostic ToWireDiagnostic(DiagnosticInfo diagnostic) => new(
        diagnostic.Code,
        diagnostic.Message,
        (CompilerDiagnosticSeverity)(byte)diagnostic.Severity,
        ToWireSpan(diagnostic.Span),
        diagnostic.SourcePath,
        [.. diagnostic.Labels.Select(label => new CompilerDiagnosticLabel(ToWireSpan(label.Span), label.Message, (CompilerDiagnosticLabelStyle)(byte)label.Style, label.FilePath))],
        [.. diagnostic.Notes.Select(note => new CompilerDiagnosticNote(note.Message, note.FilePath, note.Span is { } span ? ToWireSpan(span) : null))],
        [.. diagnostic.HelpMessages.Select(help => new CompilerDiagnosticHelp(help.Message, help.FilePath, help.Span is { } span ? ToWireSpan(span) : null))],
        [.. diagnostic.Suggestions.Select(suggestion => new CompilerDiagnosticSuggestion(suggestion.Description,
            [.. suggestion.Edits.Select(edit => new CompilerTextEdit(ToWireSpan(edit.Span), edit.NewText, edit.FilePath))]))],
        diagnostic.MacroTrace is null ? null : ToWireMacroTrace(diagnostic.MacroTrace));

    public static CompilerMacroTrace ToWireMacroTrace(MacroExpansionTraceInfo trace) => new(
        trace.MacroName,
        ToWireSpan(trace.InvocationSpan),
        trace.InvocationFilePath,
        trace.DefinitionSpan is { } span ? ToWireSpan(span) : null,
        trace.DefinitionFilePath,
        trace.Parent is null ? null : ToWireMacroTrace(trace.Parent));

    public static CompilerTextSpan ToWireSpan(TextSpanInfo span) => new(
        span.Start,
        span.Length,
        span.End,
        new CompilerTextLocation(span.StartLocation.Line, span.StartLocation.Column),
        new CompilerTextLocation(span.EndLocation.Line, span.EndLocation.Column));
}
