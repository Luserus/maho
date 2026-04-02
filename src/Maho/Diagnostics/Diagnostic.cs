using Maho.Text;

namespace Maho.Diagnostics;

/// <summary>
/// Internal diagnostic model used while the compiler is still in its analysis stages. It keeps the
/// reporting surface small and stage-agnostic until diagnostics are projected into the public API.
/// </summary>
internal sealed class Diagnostic
{
    public string DiagnosticCode { get; }
    public string Message { get; }
    public TextSpan Span { get; }
    public DiagnosticKind Kind { get; }
    public string? ExpectedText { get; }

    /// <summary>
    /// Captures one reported problem together with its stable code, rendered message, raw source
    /// span, and internal severity category.
    /// </summary>
    public Diagnostic(string diagnosticCode, string message, TextSpan span, DiagnosticKind kind, string? expectedText = null)
    {
        DiagnosticCode = diagnosticCode;
        Message = message;
        Span = span;
        Kind = kind;
        ExpectedText = expectedText;
    }
}
