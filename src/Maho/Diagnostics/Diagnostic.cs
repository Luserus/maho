using Maho.Text;

namespace Maho.Diagnostics;

internal sealed class Diagnostic
{
    public string DiagnosticCode { get; }
    public string Message { get; }
    public TextSpan Span { get; }
    public DiagnosticKind Kind { get; }

    public Diagnostic(string diagnosticCode, string message, TextSpan span, DiagnosticKind kind)
    {
        DiagnosticCode = diagnosticCode;
        Message = message;
        Span = span;
        Kind = kind;
    }
}