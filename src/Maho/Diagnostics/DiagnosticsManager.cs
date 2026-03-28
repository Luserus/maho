using System.Collections.Generic;
using Maho.Text;

namespace Maho.Diagnostics;

/// <summary> Manages the diagnostics of the compiler. </summary>
internal sealed class DiagnosticsManager
{
    private readonly List<Diagnostic> diagnostics = [];
    public IReadOnlyList<Diagnostic> Diagnostics => diagnostics;
    public bool HasErrors => diagnostics.Exists(static diagnostic => diagnostic.Kind is DiagnosticKind.Error);

    public void Report(Diagnostic diagnostic) => diagnostics.Add(diagnostic);

    public void ReportInfo(string code, string message, TextSpan span) => Report(new Diagnostic(code, message, span, DiagnosticKind.Info));
    public void ReportWarning(string code, string message, TextSpan span) => Report(new Diagnostic(code, message, span, DiagnosticKind.Warning));

    public void ReportError(string code, string message, TextSpan span) => Report(new Diagnostic(code, message, span, DiagnosticKind.Error));


    public void ReportBadToken(TextSpan span, string tokenText) =>
        ReportError("MHC0001", $"Invalid token {FormatTokenText(tokenText)}.", span);

    public void ReportUnterminatedString(TextSpan span) =>
        ReportError("MHC0002", "Unterminated string literal.", span);

    public void ReportUnterminatedCharacter(TextSpan span) =>
        ReportError("MHC0003", "Unterminated character literal.", span);

    public void ReportEmptyCharacterLiteral(TextSpan span) =>
        ReportError("MHC0004", "Character literal cannot be empty.", span);

    public void ReportExpectedToken(TextSpan span, string expected, string found, string? context = null) =>
        ReportError("MHC1001", CreateExpectedMessage(expected, found, context), span);

    public void ReportExpectedExpression(TextSpan span, string found, string? context = null) =>
        ReportError("MHC1002", CreateExpectedMessage("an expression", found, context), span);

    public void ReportExpectedIdentifier(TextSpan span, string found, string? context = null) =>
        ReportError("MHC1003", CreateExpectedMessage("an identifier", found, context), span);

    public void ReportExpectedType(TextSpan span, string found, string? context = null) =>
        ReportError("MHC1004", CreateExpectedMessage("a type", found, context), span);

    public void ReportUnexpectedToken(TextSpan span, string found) =>
        ReportExpectedToken(span, "valid syntax", found);

    public void ReportMissingToken(TextSpan span, string expected) =>
        ReportExpectedToken(span, expected, "<missing>");

    private static string CreateExpectedMessage(string expected, string found, string? context)
    {
        if (string.IsNullOrWhiteSpace(context))
            return $"Expected {expected}, found {FormatTokenText(found)}.";

        return $"Expected {expected} {context}, found {FormatTokenText(found)}.";
    }

    private static string FormatTokenText(string tokenText)
    {
        if (string.IsNullOrEmpty(tokenText))
            return "<end of file>";

        return tokenText switch
        {
            "<end of file>" or "<missing>" => tokenText,
            _ => $"'{tokenText}'"
        };
    }
}
