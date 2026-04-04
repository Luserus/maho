using Maho.Text;

namespace Maho.Diagnostics;

/// <summary>
/// Internal diagnostic model used while the compiler is still in its analysis stages. It keeps the
/// reporting surface small and stage-agnostic until diagnostics are projected into the public API.
/// </summary>
internal sealed class Diagnostic
{
    private readonly string? message;
    private readonly DiagnosticText foundText;
    private readonly string? context;

    public string DiagnosticCode { get; }
    public TextSpan Span { get; }
    public DiagnosticKind Kind { get; }
    public string? ExpectedText { get; }
    public DiagnosticMessageKind MessageKind { get; }

    /// <summary> Materializes the final diagnostic message at the presentation boundary. </summary>
    public string Message => MessageKind switch
    {
        DiagnosticMessageKind.Fixed => message ?? string.Empty,
        DiagnosticMessageKind.Expected => CreateExpectedMessage(ExpectedText ?? string.Empty, foundText, context),
        DiagnosticMessageKind.BadToken => $"Invalid token {FormatTokenText(foundText)}.",
        _ => string.Empty
    };

    /// <summary>
    /// Captures one reported problem together with its stable code, rendered message, raw source
    /// span, and internal severity category.
    /// </summary>
    public Diagnostic(string diagnosticCode, string message, TextSpan span, DiagnosticKind kind, string? expectedText = null)
    {
        DiagnosticCode = diagnosticCode;
        this.message = message;
        Span = span;
        Kind = kind;
        ExpectedText = expectedText;
        MessageKind = DiagnosticMessageKind.Fixed;
    }

    public Diagnostic(string diagnosticCode, DiagnosticText foundText, TextSpan span, DiagnosticKind kind)
    {
        DiagnosticCode = diagnosticCode;
        this.foundText = foundText;
        Span = span;
        Kind = kind;
        MessageKind = DiagnosticMessageKind.BadToken;
    }

    public Diagnostic(string diagnosticCode, string expectedText, DiagnosticText foundText, TextSpan span, DiagnosticKind kind, string? context = null)
    {
        DiagnosticCode = diagnosticCode;
        ExpectedText = expectedText;
        this.foundText = foundText;
        Span = span;
        Kind = kind;
        this.context = context;
        MessageKind = DiagnosticMessageKind.Expected;
    }

    private static string CreateExpectedMessage(string expected, DiagnosticText found, string? context)
    {
        if (string.IsNullOrWhiteSpace(context))
            return $"Expected {expected}, found {FormatTokenText(found)}.";

        return $"Expected {expected} {context}, found {FormatTokenText(found)}.";
    }

    private static string FormatTokenText(DiagnosticText tokenText)
    {
        string materialized = tokenText.Materialize();

        return tokenText.Kind switch
        {
            DiagnosticTextKind.EndOfFile or DiagnosticTextKind.MissingToken => materialized,
            _ when string.IsNullOrEmpty(materialized) => "<end of file>",
            _ => $"'{materialized}'"
        };
    }
}

internal enum DiagnosticMessageKind : byte
{
    Fixed,
    Expected,
    BadToken
}
