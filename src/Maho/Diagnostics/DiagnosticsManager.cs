using System.Collections.Generic;
using Maho.Text;

namespace Maho.Diagnostics;

/// <summary> Manages the diagnostics of the compiler. </summary>
internal sealed class DiagnosticsManager
{
    private readonly List<Diagnostic> diagnostics = [];

    public void Report(Diagnostic diagnostic) => diagnostics.Add(diagnostic);

    public void ReportInfo(string code, string message, TextSpan span) => Report(new Diagnostic(code, message, span, DiagnosticKind.Info));
    public void ReportWarning(string code, string message, TextSpan span) => Report(new Diagnostic(code, message, span, DiagnosticKind.Warning));

    public void ReportError(string code, string message, TextSpan span) => Report(new Diagnostic(code, message, span, DiagnosticKind.Error));


    public void ReportUnexpectedToken(TextSpan span, string found) => ReportError("MHC0001", $"Unexpected token. '{found}' is not valid in this context.", span);
        
    public void ReportMissingToken(TextSpan span, string expected) => ReportError("MHC0002", $"Missing token. Expected '{expected}'.", span);
}