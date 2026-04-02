using System.Collections.Generic;
using Maho.Text;

namespace Maho.Diagnostics;

/// <summary>
/// Aggregates diagnostics produced during analysis and centralizes the message shapes used by
/// lexer and parser code. This keeps stage logic focused on detecting problems rather than on
/// formatting user-visible text.
/// </summary>
internal sealed class DiagnosticsManager
{
    private readonly List<Diagnostic> diagnostics = [];

    /// <summary>
    /// Gets diagnostics in report order so downstream projection can preserve stable ordering when
    /// two diagnostics share the same source location.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics => diagnostics;

    /// <summary>
    /// Indicates whether any reported diagnostic should be treated as a failing analysis condition.
    /// </summary>
    public bool HasErrors => diagnostics.Exists(static diagnostic => diagnostic.Kind is DiagnosticKind.Error);

    /// <summary>
    /// Appends an already-constructed diagnostic to the shared collection.
    /// </summary>
    public void Report(Diagnostic diagnostic) => diagnostics.Add(diagnostic);

    /// <summary>
    /// Reports a non-failing informational diagnostic using the shared internal model.
    /// </summary>
    public void ReportInfo(string code, string message, TextSpan span) => Report(new Diagnostic(code, message, span, DiagnosticKind.Info));

    /// <summary>
    /// Reports a warning diagnostic using the shared internal model.
    /// </summary>
    public void ReportWarning(string code, string message, TextSpan span) => Report(new Diagnostic(code, message, span, DiagnosticKind.Warning));

    /// <summary>
    /// Reports an error diagnostic using the shared internal model.
    /// </summary>
    public void ReportError(string code, string message, TextSpan span) => Report(new Diagnostic(code, message, span, DiagnosticKind.Error));

    /// <summary>
    /// Reports an error diagnostic that also preserves the expected syntax text for downstream
    /// renderers that want to produce more specific remediation hints.
    /// </summary>
    private void ReportExpected(string code, string expected, string found, TextSpan span, string? context = null) =>
        Report(new Diagnostic(code, CreateExpectedMessage(expected, found, context), span, DiagnosticKind.Error, expected));


    /// <summary>
    /// Reports an invalid token emitted by the lexer, preserving the offending text when possible.
    /// </summary>
    public void ReportBadToken(TextSpan span, string tokenText) =>
        ReportError("MH0000", $"Invalid token {FormatTokenText(tokenText)}.", span);

    /// <summary>
    /// Reports a string literal that could not be closed before the lexer had to recover.
    /// </summary>
    public void ReportUnterminatedString(TextSpan span) =>
        ReportError("MH0001", "Unterminated string literal.", span);

    /// <summary>
    /// Reports a character literal that could not be closed before the lexer had to recover.
    /// </summary>
    public void ReportUnterminatedCharacter(TextSpan span) =>
        ReportError("MH0002", "Unterminated character literal.", span);

    /// <summary>
    /// Reports a character literal whose delimiters contain no payload.
    /// </summary>
    public void ReportEmptyCharacterLiteral(TextSpan span) =>
        ReportError("MH0003", "Character literal cannot be empty.", span);

    /// <summary>
    /// Reports a parser recovery site where a specific token kind was required.
    /// </summary>
    public void ReportExpectedToken(TextSpan span, string expected, string found, string? context = null) =>
        ReportExpected("MH0004", expected, found, span, context);

    /// <summary>
    /// Reports a parser recovery site where an expression was needed to continue meaningfully.
    /// </summary>
    public void ReportExpectedExpression(TextSpan span, string found, string? context = null) =>
        ReportExpected("MH0005", "an expression", found, span, context);

    /// <summary>
    /// Reports a parser recovery site where an identifier-shaped token was required.
    /// </summary>
    public void ReportExpectedIdentifier(TextSpan span, string found, string? context = null) =>
        ReportExpected("MH0006", "an identifier", found, span, context);

    /// <summary>
    /// Reports a parser recovery site where type syntax was required.
    /// </summary>
    public void ReportExpectedType(TextSpan span, string found, string? context = null) =>
        ReportExpected("MH0007", "a type", found, span, context);

    /// <summary>
    /// Reports a parser recovery site where a declaration or type body was required.
    /// </summary>
    public void ReportExpectedBody(TextSpan span, string expected, string found, string? context = null) =>
        ReportExpected("MH0008", expected, found, span, context);

    /// <summary>
    /// Reports a parser recovery site where parameter syntax was required.
    /// </summary>
    public void ReportExpectedParameter(TextSpan span, string found, string? context = null) =>
        ReportExpected("MH0009", "a parameter", found, span, context);

    /// <summary>
    /// Reports a parser recovery site where a type parameter syntax was required.
    /// </summary>
    public void ReportExpectedTypeParameter(TextSpan span, string found, string? context = null) =>
        ReportExpected("MH0010", "a type parameter", found, span, context);

    /// <summary>
    /// Reports a generic parser mismatch when no narrower expectation is available.
    /// </summary>
    public void ReportUnexpectedToken(TextSpan span, string found) =>
        ReportExpectedToken(span, "valid syntax", found);

    /// <summary>
    /// Reports a parser-synthesized missing token using a standardized found-text placeholder so
    /// renderers and tests can recognize recovery artifacts consistently.
    /// </summary>
    public void ReportMissingToken(TextSpan span, string expected) =>
        ReportExpectedToken(span, expected, "<missing>");

    /// <summary>
    /// Builds the shared parser message shape so every expected-X diagnostic reads consistently,
    /// adding contextual wording only when it disambiguates the recovery site.
    /// </summary>
    private static string CreateExpectedMessage(string expected, string found, string? context)
    {
        // Keep parser recovery diagnostics on one message template so renderers and tests can rely
        // on code/message pairs instead of every parser site inventing new phrasing.
        if (string.IsNullOrWhiteSpace(context))
            return $"Expected {expected}, found {FormatTokenText(found)}.";

        return $"Expected {expected} {context}, found {FormatTokenText(found)}.";
    }

    /// <summary>
    /// Normalizes token text for diagnostics, preserving sentinel values such as
    /// <c>&lt;missing&gt;</c> while quoting ordinary source text.
    /// </summary>
    private static string FormatTokenText(string tokenText)
    {
        if (string.IsNullOrEmpty(tokenText))
            return "<end of file>";

        return tokenText switch
        {
            // Preserve synthetic sentinels verbatim so diagnostics can distinguish parser-inserted
            // recovery tokens from ordinary quoted source text.
            "<end of file>" or "<missing>" => tokenText,
            _ => $"'{tokenText}'"
        };
    }
}
